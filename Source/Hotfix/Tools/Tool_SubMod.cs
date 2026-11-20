using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace Hotfix;

internal sealed partial class Tool_SubMod() : Tool_Base("SubMod",
    """
    Manages sub-mods: small mods that Hotfix loads itself. Each is a folder named after its id under the sub-mods folder (see Files in the system prompt), and the user can turn each one on or off in the mod settings.
    create makes the folder with SubMod.xml, a folder named after the running game's version (such as 1.7) and an empty Common folder. The version folder holds these empty folders: Source for C# files, Assemblies for the DLL that build writes, Defs/<id> and Patches/<id> for XML, and Textures, Sounds and Languages as in an ordinary mod. Fill them with Write and Edit.
    Versions work as in an ordinary mod: the game reads the folder of its own version and Common, and a file in the version folder wins over the same path in Common. Common may hold the same folders except Source, for what all versions share. Any other DLL in an Assemblies folder is loaded as a library.
    SupportedVersions in SubMod.xml lists the game versions a sub-mod supports. It is loaded only while the running game's version is listed there and has its folder. To support another version, copy a version folder under the new name, change what differs, add the version to SupportedVersions, and build while running that game version.
    build compiles every .cs file under Source of the running game's version folder into one assembly. For a sub-mod that has only code it applies the assembly right away: [HarmonyPatch] classes are patched and [StaticConstructorOnStartup] classes run. The files are ordinary mod code, except that Hotfix creates the Harmony instance and calls PatchAll, and that a Mod subclass is not instantiated. Private members can be used directly. Build again after changing a file of the sub-mod.
    The game reads defs, XML patches, textures, sounds and translations only while it starts. So once a sub-mod has any file in Defs, Patches, Textures, Sounds or Languages, nothing of it changes in the running game any more: build only checks and saves the code, an earlier build that is already applied keeps running, and the code and the files are loaded together on the next game start. Tell the user to restart the game before adding such files if you still need to try code in the running game. Sub-mods load after all other mods.
    enable and disable turn a sub-mod on or off, and delete removes its folder. A sub-mod with only code follows right away. One whose defs, XML patches or assets the game read when it started stays loaded as a whole, code included, until the next game start.
    requires in SubMod.xml lists the packageIds of the mods a sub-mod needs; while one of them is not active the sub-mod is not loaded. build adds the mods whose assemblies the code references.
    """,
    """
    {
      "type": "object",
      "properties": {
        "action": { "type": "string", "enum": ["create", "build", "enable", "disable", "delete"] },
        "id": { "type": "string", "description": "Folder name of the sub-mod: letters, digits, underscores" },
        "name": { "type": "string", "description": "For create: the name shown to the user" },
        "description": { "type": "string", "description": "For create: one line on what the sub-mod does, shown to the user" },
        "requires": { "type": "array", "items": { "type": "string" }, "description": "For create: packageIds of the mods it needs" }
      },
      "required": ["action", "id"]
    }
    """)
{
    public override bool NeedsApproval(JsonNode arguments, ToolContext context) => arguments.GetString("action") != "create";

    public override async Task<string> RunAsync(JsonNode arguments, ToolContext context, CancellationToken ct)
    {
        var id = Required(arguments, "id");
        if (!Regex_Id().IsMatch(id))
            throw new ArgumentException("id may contain only letters, digits and underscores, at most 40 characters.");
        var action = Required(arguments, "action");
        if (action == "create")
        {
            // 임시 폴더의 이름이 대화의 id
            SubModManager.Create(id, arguments.GetString("name", id), arguments.GetString("description", ""),
                arguments.GetArray("requires").Select(item => item?.ToString()).OfType<string>(), Path.GetFileName(context.Scratch));
            var folder = SubModManager.Folder(id);
            return $"Created the sub-mod {id} at {folder} with SubMod.xml and these empty folders:\n"
                + string.Join("\n", SubModManager.Structure(id).Select(part => " - " + Path.Combine(folder, part)));
        }

        var sub = SubModManager.Find(id) ?? throw new DirectoryNotFoundException($"There is no sub-mod named {id}.");
        switch (action)
        {
            case "build":
                if (!SubModManager.Supported(sub))
                    throw new InvalidOperationException(Unsupported(sub));
                if (SubModManager.Misplaced(id) is { Count: > 0 } misplaced)
                    throw new InvalidOperationException($"Move these files into a Defs/{id} or Patches/{id} folder. Files of different sub-mods must not share a path:\n"
                        + string.Join("\n", misplaced));
                var image = await Task.Run(() => SubModManager.Compile(sub), ct);
                var patched = SubModManager.Install(sub, image);
                return $"Built the sub-mod {id} (revision {sub.Revision}).\n" + Code(sub, image is not null, patched) + Content(sub);
            case "enable":
                SubModManager.SetEnabled(sub, true);
                return $"Turned on the sub-mod {id}.\n"
                    + Code(sub, File.Exists(SubModManager.Dll(id)), SubModCode.IsApplied(id) ? SubModCode.Patched(id) : null) + Content(sub);
            case "disable":
                SubModManager.SetEnabled(sub, false);
                return $"Turned off the sub-mod {id}." + Removed(id);
            case "delete":
                SubModManager.Delete(sub);
                return $"Deleted the sub-mod {id}." + Removed(id);
            default:
                throw new ArgumentException("action must be create, build, enable, disable or delete");
        }
    }

    private static string Code(SubMod sub, bool hasCode, IReadOnlyList<string>? patched)
    {
        if (!hasCode)
            return "It has no code.";
        if (patched is null)
            return !sub.Enabled ? "The code is not applied because the sub-mod is off."
                : !SubModManager.Supported(sub) ? "The code is not applied. " + Unsupported(sub)
                : SubModManager.MissingRequires(sub) is { Count: > 0 } missing
                    ? $"The code is not applied because these mods are not active: {string.Join(", ", missing)}"
                : "The code is saved but not applied: this sub-mod has defs, XML patches or assets, so its code is loaded together with them on the next game start."
                    + (SubModCode.IsApplied(sub.Id) ? " The code that was applied before keeps running until then." : "");
        var text = patched.Count > 0 ? "The code is applied now. Patched methods:\n" + string.Join("\n", patched) : "The code is applied now. It patched no methods.";
        var components = SubModCode.AssemblyOf(sub.Id)?.GetTypes().Where(type => !type.IsAbstract
            && (typeof(GameComponent).IsAssignableFrom(type) || typeof(RimWorld.Planet.WorldComponent).IsAssignableFrom(type)
                || typeof(MapComponent).IsAssignableFrom(type))).Select(type => type.Name).ToList();
        return components is { Count: > 0 }
            ? text + $"\nThe game creates {string.Join(", ", components)} when a save is loaded or a new game starts, not now."
            : text;
    }

    private static string Unsupported(SubMod sub) =>
        $"The sub-mod {sub.Id} does not support game version {VersionControl.CurrentVersionStringWithoutBuild}: SupportedVersions in SubMod.xml must list it "
        + $"(now: {string.Join(", ", sub.SupportedVersions)}) and the folder {SubModManager.VersionFolder(sub.Id)} must exist.";

    private static string Content(SubMod sub)
    {
        var folder = SubModManager.Folder(sub.Id);
        var files = SubModManager.ContentFiles(sub.Id).Select(file => Path.GetRelativePath(folder, file)).ToList();
        return files.Count == 0 || SubModManager.Status(sub) != SubModStatus.Restart
            ? ""
            : "\nThese files apply on the next game start:\n" + string.Join("\n", files.Take(50))
                + (files.Count > 50 ? $"\nand {files.Count - 50} more" : "");
    }

    private static string Removed(string id) => SubModManager.CanRemove(id)
        ? " Its Harmony patches are undone."
        : " The game read its defs, XML patches or assets when it started, so all of it, code included, stays loaded until the next game start.";

    [GeneratedRegex("^[A-Za-z0-9_]{1,40}$")]
    private static partial Regex Regex_Id();
}
