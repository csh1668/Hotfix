using System;
using Rimact;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Hotfix;

internal sealed record ProfileRow(HotfixSettings Settings, LlmProfile Profile, Action<LlmProfile> OnEdit) : UIComponent
{
    protected internal override Node Build()
    {
        var t = UseTheme();
        var settings = Settings;
        var profile = Profile;
        var edit = OnEdit;
        var (name, providerId, model) = UseExternal(() => (profile.Name, profile.ProviderId, profile.Model));
        var provider = profile.Provider?.Label ?? providerId;

        return Item(name, model.Length > 0 ? $"{provider} · {model}" : provider,
            actions: Row(
                IconButton(Glyph.Pencil, () => edit(profile), "Hotfix_EditProfile".Translate()),
                IconButton(Glyph.Copy, Duplicate, "Hotfix_Duplicate".Translate()),
                IconButton(Glyph.Trash, () => settings.Profiles.Remove(profile), "Hotfix_Delete".Translate())
            ).Gap(t.Space(1)).Align(Align.Center));

        void Duplicate()
        {
            var copy = new LlmProfile();
            copy.CopyFrom(profile);
            copy.Name += " (2)";
            settings.Profiles.Insert(settings.Profiles.IndexOf(profile) + 1, copy);
            edit(copy);
        }
    }
}
