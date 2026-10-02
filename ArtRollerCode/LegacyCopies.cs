using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ArtRoller;

/// <summary>
/// Finds rolls by convention, including those of mods that pasted in their own copy of Art Roller.
/// Reading a copy's rolls keeps both copies computing the same colours; otherwise whichever
/// NCard.Reload postfix runs last wins, and ours would reset that mod's cards to neutral.
///
/// Repeats whenever the loaded-mod count changes, since a mod that does not depend on Art Roller
/// can finish loading after it.
/// </summary>
internal static class LegacyCopies
{
    /// <summary>Where pasted copies keep personal rolls when they do not override it.</summary>
    private const string LegacyUserSaveDirectory = "user://card_hsv_data";

    internal static readonly List<string> DefaultsDirectories = [];
    internal static readonly List<string> PortraitDirectories = [];
    internal static readonly Dictionary<string, CardHsvData> UserRolls = new();

    private static readonly HashSet<string> SeenMods = [];
    private static int _loadedModCount = -1;
    private static bool _legacyUserSavesLoaded;

    internal static void Discover()
    {
        // Runs on every roll lookup, so the unchanged case only counts.
        int count = ModManager.GetLoadedMods().Count();
        if (count == _loadedModCount) return;
        _loadedModCount = count;
        CardArtRoller.ClearDefaultsCache();

        if (!_legacyUserSavesLoaded)
        {
            _legacyUserSavesLoaded = true;
            CardArtRoller.LoadRollsFrom(ProjectSettings.GlobalizePath(LegacyUserSaveDirectory), UserRolls);
        }

        foreach (var mod in ModManager.GetLoadedMods())
        {
            string? id = mod.manifest?.id;
            if (id == null || id == MainFile.ModId || !SeenMods.Add(id)) continue;

            AddIfPresent(DefaultsDirectories, $"res://{id}/ArtRoller");
            AddIfPresent(PortraitDirectories, $"res://{id}/images/card_portraits");

            // Spireverse, and copies of it, saved personal rolls beside their DLL.
            CardArtRoller.LoadRollsFrom(Path.Combine(mod.path, "ArtRoller"), UserRolls);
        }
    }

    private static void AddIfPresent(List<string> list, string directory)
    {
        if (DirAccess.DirExistsAbsolute(directory) && !list.Contains(directory))
            list.Add(directory);
    }

}
