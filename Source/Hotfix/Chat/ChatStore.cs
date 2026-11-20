using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hotfix.Data;

namespace Hotfix;

// Text는 검색 대상: 사용자가 쓴 글과 모델이 답한 글
internal sealed record ChatInfo(string Id, string Title, DateTime Time, string Text);

internal sealed record ChatSave(string? ProfileId, LlmTokenUsage? Usage, int ContextTokens, int ContextIndex,
    List<ChatMessage> Messages, double Cost = 0);

internal sealed class ChatStore(string directory)
{
    // 저장된 대화, 최근 것부터
    public List<ChatInfo> History()
    {
        var chats = new List<ChatInfo>();
        foreach (var file in Files())
        {
            if (ReadFile(file.FullName) is not { } saved)
                continue;
            var text = saved.Messages.Select(message => message switch
            {
                ChatMessage_User user => user.Text,
                ChatMessage_Assistant assistant => assistant.Message.Text(),
                _ => "",
            }).Where(part => part.Length > 0);
            chats.Add(new ChatInfo(IdOf(file), TitleOf(saved.Messages.OfType<ChatMessage_User>().FirstOrDefault()?.Text ?? ""),
                file.LastWriteTime, string.Join("\n", text)));
        }
        return chats;
    }

    public string? Latest() => Files().FirstOrDefault() is { } file ? IdOf(file) : null;

    public ChatSave? Read(string id) => ReadFile(PathOf(id));

    public void Write(string id, ChatSave save)
    {
        try
        {
            Directory.CreateDirectory(directory);
            XmlHelper.Save(PathOf(id), save);
        }
        catch (Exception e)
        {
            Logger.Warning($"Failed to save the chat: {e.Message}");
        }
    }

    public void Delete(string id) => File.Delete(PathOf(id));

    private string PathOf(string id) => Path.Combine(directory, id + ".xml");

    private static string IdOf(FileInfo file) => Path.GetFileNameWithoutExtension(file.Name);

    private IEnumerable<FileInfo> Files() => Directory.Exists(directory)
        ? new DirectoryInfo(directory).GetFiles("*.xml").OrderByDescending(file => file.LastWriteTimeUtc)
        : [];

    private static ChatSave? ReadFile(string path)
    {
        try
        {
            return XmlHelper.Load<ChatSave>(path);
        }
        catch (Exception e)
        {
            Logger.Warning($"Failed to read the chat {Path.GetFileName(path)}: {e.Message}");
            return null;
        }
    }

    // 첫 줄의 앞부분
    private static string TitleOf(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > 80 ? line[..80] : line;
    }
}
