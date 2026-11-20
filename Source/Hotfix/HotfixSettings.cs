using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Hotfix;

public class HotfixSettings : ModSettings
{
    private const float DefaultChatOpacity = 0.95f;

    public List<LlmProfile> Profiles = [];
    public Dictionary<LlmTask, string> TaskModels = [];
    public float ChatOpacity = DefaultChatOpacity;
    public PermissionMode PermissionMode = PermissionMode.Auto;
    public List<PromptPreset> Presets = [];
    public string PresetId = PromptPreset.DefaultId;
    public bool MemoryEnabled = true;
    private string? _responseLanguage;

    public string ResponseLanguage
    {
        get => (_responseLanguage ??= LanguageDatabase.activeLanguage?.FriendlyNameEnglish) ?? "";
        set => _responseLanguage = value;
    }

    public LlmProfile? MainProfile => TaskProfile(LlmTask.Main) ?? Profiles.FirstOrDefault();

    public PromptPreset Preset => PromptPreset.BuiltIn.Concat(Presets).FirstOrDefault(p => p.Id == PresetId) ?? PromptPreset.BuiltIn[0];

    public LlmProfile? FindProfile(string? id) => Profiles.Find(p => p.Id == id);

    public LlmProfile? TaskProfile(LlmTask task) => FindProfile(TaskModels.GetValueOrDefault(task));

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref Profiles, "profiles", LookMode.Deep);
        Scribe_Collections.Look(ref TaskModels, "taskModels", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref ChatOpacity, "chatOpacity", DefaultChatOpacity);
        Scribe_Values.Look(ref PermissionMode, "permissionMode", PermissionMode.Auto);
        Scribe_Collections.Look(ref Presets, "presets", LookMode.Deep);
        Scribe_Values.Look(ref PresetId, "presetId", PromptPreset.DefaultId);
        Scribe_Values.Look(ref MemoryEnabled, "memoryEnabled", true);
        Scribe_Values.Look(ref _responseLanguage, "responseLanguage");
        Profiles ??= [];
        TaskModels ??= [];
        Presets ??= [];
    }
}
