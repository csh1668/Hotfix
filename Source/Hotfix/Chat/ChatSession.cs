using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hotfix.Data;

namespace Hotfix;

internal sealed record QueuedInput(string Text, string? View, ChatImage? Image);

internal sealed partial class ChatSession(ChatStore store)
{
    public const int MaxRetries = 10;
    private const int MaxOutputRecoveries = 3;
    private const int MaxSummaryFailures = 3;
    private const int MaxSummaryTruncations = 3;
    private const int SummaryReserve = 33_000;
    private const int MaxToolResult = 50_000;
    private const int MaxRequestImages = 5;
    private const int ImageTokens = 1500;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(32);

    private readonly List<ChatMessage> _messages = [];
    private readonly List<QueuedInput> _queue = [];
    private SortedDictionary<int, LivePart>? _live;
    private CancellationTokenSource? _cts;
    private LlmProfile? _profile;
    private string? _systemPrompt;
    private (string Style, string Language, bool Memory) _promptKey;
    private string _id = NewId();
    private string? _lastView;
    private object? _logMark = GameLog.Mark();
    private ToolContext? _context;
    private TaskCompletionSource<bool>? _answer;
    private TaskCompletionSource<IReadOnlyList<IReadOnlyList<string>>?>? _reply;
    private int _recoveries;
    private bool _overflowed;
    private int _summaryFailures;
    private int _contextTokens;
    private int _contextIndex;
    private long _started;

    public IReadOnlyList<Tool_Base> Tools { get; init; } = [];
    public required ToolPaths Paths { get; init; }
    public required MemoryStore Memory { get; init; }
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public string Id => _id;
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public IReadOnlyList<QueuedInput> Queue => _queue;
    public string Scratch => Context.Scratch;
    public string Draft { get; set; } = "";
    public LlmProfile? Profile => _profile ?? HotfixMod.Settings.MainProfile;
    public LlmTokenUsage? Usage { get; private set; }
    public double Cost { get; private set; }
    public int Version { get; private set; }
    public bool Summarizing { get; private set; }
    public ToolApproval? Approval { get; private set; }
    public PendingQuestion? Question { get; private set; }
    public int RetryAttempt { get; private set; }
    public LlmPart_ToolCall? RunningCall { get; private set; }
    public bool Judging { get; private set; }
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);
    public bool Busy => _cts is not null;
    public bool Empty => _messages.Count == 0 && _queue.Count == 0 && !Busy;
    public LlmError? Error => !Busy && _messages.Count > 0 && _messages[^1] is ChatMessage_Error error ? error.Error : null;

    public LlmMessage? Live => _live is null
        ? null
        : new LlmMessage(LlmRole.Assistant, _live.Values.Select(part => part.ToPart()).ToList());

    public void Send(string text, ChatImage? image = null)
    {
        if (!Bind())
            return;
        var view = View();
        _queue.Add(new QueuedInput(text, view.Length == 0 || view == _lastView ? null : view, image));
        _lastView = view;
        Version++;
        Reply(null);
        if (!Busy)
            Run();
    }

    private string View()
    {
        var view = GameView.Describe();
        var log = GameLog.Since(_logMark, 5);
        _logMark = GameLog.Mark();
        return log.Length > 0 ? $"{view}\nNew warnings and errors in the game log:\n{log}" : view;
    }

    public void Retry()
    {
        if (Error is null || !Bind())
            return;
        _messages.RemoveAt(_messages.Count - 1);
        Run();
    }

    public void Cancel() => _cts?.Cancel();

    public void Answer(bool allow) => _answer?.TrySetResult(allow);

    public void Reply(IReadOnlyList<IReadOnlyList<string>>? answers) => _reply?.TrySetResult(answers);

    public void Unqueue(int index)
    {
        if (index >= _queue.Count)
            return;
        _queue.RemoveAt(index);
        Version++;
    }

    public void Discard()
    {
        _cts?.Cancel();
        _cts = null;
        store.Delete(_id);
        try
        {
            if (Directory.Exists(Context.Scratch))
                Directory.Delete(Context.Scratch, true);
        }
        catch (IOException e)
        {
            Logger.Warning($"Failed to delete the scratch folder: {e.Message}");
        }
    }

    private ToolContext Context => _context ??= new ToolContext(Paths, Path.Combine(Paths.ScratchRoot, _id), Ask);

    private bool Bind() => (_profile ??= HotfixMod.Settings.MainProfile) is not null;

    private LlmProfile ProfileFor(LlmTask task) => HotfixMod.Settings.TaskProfile(task) ?? _profile!;

    #region Loop

    private enum Step
    {
        // 다시 호출
        Continue,
        // 한 차례가 끝남
        Done,
        Failed,
    }

    private async void Run()
    {
        var cts = _cts = new CancellationTokenSource();
        _started = Stopwatch.GetTimestamp();
        _recoveries = 0;
        _overflowed = false;
        while (true)
        {
            foreach (var input in _queue)
            {
                if (input.View is not null)
                    _messages.Add(new ChatMessage_Meta(Prompts.View(input.View)));
                _messages.Add(new ChatMessage_User(input.Text, input.Image is null ? null : [input.Image]));
            }
            _queue.Clear();
            Commit();

            var key = (HotfixMod.Settings.Preset.Text, HotfixMod.Settings.ResponseLanguage, HotfixMod.Settings.MemoryEnabled);
            if (_systemPrompt is null || key != _promptKey)
            {
                _promptKey = key;
                _systemPrompt = Prompts.System(_profile!, Context, key.Text, key.ResponseLanguage, key.MemoryEnabled ? Memory : null);
            }
            var step = await Next(cts.Token);
            if (_cts != cts)
                return;
            if (step == Step.Continue)
                continue;
            if (step == Step.Failed || _queue.Count == 0)
                break;
            if (cts.IsCancellationRequested)
                cts = _cts = new CancellationTokenSource();
            _started = Stopwatch.GetTimestamp();
            _recoveries = 0;
        }

        _cts = null;
        Commit();
    }

    private async Task<Step> Next(CancellationToken ct)
    {
        if (NeedsSummary())
            await Summarize(ct, true);

        var response = await Call(_profile!, Request(_profile!, RequestMessages(Boundary(), _messages.Count)), true, ct);
        var live = _live!;
        _live = null;

        if (!response.IsSuccess)
        {
            var error = response.Error ?? new LlmError(LlmErrorKind.Unknown, "Empty response");
            if (error.Kind == LlmErrorKind.ContextTooLong && !_overflowed && !ct.IsCancellationRequested)
            {
                _overflowed = true;
                if (await Summarize(ct, true))
                    return Step.Continue;
            }
            if (!ct.IsCancellationRequested)
                return Fail(error);

            var text = string.Concat(live.Values.Where(part => !part.Reasoning).Select(part => part.Text.ToString()));
            if (!string.IsNullOrWhiteSpace(text))
                _messages.Add(new ChatMessage_Assistant(new LlmMessage(LlmRole.Assistant, [new LlmPart_Text(text)])));
            AddInterrupted();
            return Step.Done;
        }

        _overflowed = false;
        var message = response.Message!;
        if (message.Parts.Count > 0)
            _messages.Add(new ChatMessage_Assistant(message));
        if (response.TokenUsage is { } usage)
        {
            AddUsage(usage, _profile!);
            _contextTokens = usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens + usage.OutputTokens;
            _contextIndex = _messages.Count;
        }

        var calls = message.ToolCalls().ToList();
        if (calls.Count > 0)
        {
            Version++;
            var cut = response.StopReason == LlmStopReason.MaxTokens;
            var results = cut
                ? calls.Select(call => new LlmPart_ToolResult(call.Id, call.Name, Prompts.ToolCutOff, true)).ToList()
                : await RunTools(calls, ct);
            var images = Context.Attachments.ToList();
            Context.Attachments.Clear();
            _messages.Add(new ChatMessage_ToolResults(results, images.Count > 0 ? images : null));
            if (ct.IsCancellationRequested)
            {
                AddInterrupted();
                return Step.Done;
            }
            if (!cut)
            {
                _recoveries = 0;
            }
            else if (_recoveries++ >= MaxOutputRecoveries)
            {
                _messages.Add(new ChatMessage_Notice(ChatNotice.OutputLimit));
                return Step.Done;
            }
            return Step.Continue;
        }

        if (response.StopReason == LlmStopReason.MaxTokens)
        {
            if (_recoveries++ < MaxOutputRecoveries)
            {
                _messages.Add(new ChatMessage_Meta(Prompts.Continue));
                return Step.Continue;
            }
            _messages.Add(new ChatMessage_Notice(ChatNotice.OutputLimit));
        }
        else if (response.StopReason == LlmStopReason.ContentFiltered)
        {
            _messages.Add(new ChatMessage_Notice(ChatNotice.Refused));
        }
        return Step.Done;
    }

    private Step Fail(LlmError error)
    {
        _messages.Add(new ChatMessage_Error(error));
        return Step.Failed;
    }

    private void AddInterrupted()
    {
        _messages.Add(new ChatMessage_Meta(Prompts.Interrupted));
        _messages.Add(new ChatMessage_Notice(ChatNotice.Interrupted));
    }

    private void AddUsage(LlmTokenUsage usage, LlmProfile profile)
    {
        Usage = Usage is null ? usage : Usage + usage;
        Cost += profile.CostOf(usage);
    }

    private void Commit()
    {
        Version++;
        Save();
    }

    #endregion

    #region Save

    private static string NewId() => $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

    // 새로 만든 세션에 저장된 대화를 불러온다
    public bool Load(string id)
    {
        if (store.Read(id) is not { } saved)
            return false;
        _id = id;
        _messages.AddRange(saved.Messages);
        _profile = HotfixMod.Settings.FindProfile(saved.ProfileId);
        Usage = saved.Usage;
        Cost = saved.Cost;
        _contextTokens = saved.ContextTokens;
        _contextIndex = saved.ContextIndex;
        return true;
    }

    private void Save()
    {
        if (_messages.Count > 0)
            store.Write(_id, new ChatSave(_profile?.Id, Usage, _contextTokens, _contextIndex, _messages, Cost));
    }

    #endregion
}
