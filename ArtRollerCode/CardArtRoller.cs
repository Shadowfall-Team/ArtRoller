using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using MegaCrit.Sts2.Core.Models;

namespace ArtRoller;

/// <summary>
/// The public entry point. A mod keeping its rolls in <c>res://{modId}/ArtRoller</c> needs none of
/// it; the Register methods are for folders anywhere else.
/// </summary>
public static class CardArtRoller
{
    public static Dictionary<string, CardHsvData> CardHsvModifiers { get; } = new();

    /// <summary>
    /// Personal rolls saved from the editor. Kept in user data rather than beside the DLL because
    /// Steam replaces a Workshop item's folder wholesale when it updates.
    /// </summary>
    public const string SaveDirectory = "user://ArtRoller";

    /// <summary>
    /// Where "Save Default" writes, so a mod author can point it straight at their repo's packed
    /// rolls folder. Empty means <see cref="SaveDirectory"/>/defaults.
    /// </summary>
    public static string DefaultsOutputDirectory
    {
        get => _defaultsOutputDirectory;
        set
        {
            _defaultsOutputDirectory = value;
            ClearDefaultsCache();
        }
    }
    private static string _defaultsOutputDirectory = "";

    private const string ConfigFileName = "card_art_roller_config.cfg";

    private static readonly List<string> DefaultsDirectories = [];
    private static readonly List<string> PortraitDirectories = [];

    /// <summary>
    /// Every default looked up so far, misses included as null. Lookups run each time a card is
    /// drawn or its portrait path is read, far too often to go to disk.
    /// </summary>
    private static readonly Dictionary<string, CardHsvData?> DefaultsCache = new();

    /// <summary>
    /// These are keys cleared with Clear Default this session. A copy packed into a mod's <c>.pck</c> cannot be
    /// deleted until a rebuild, so it is hidden instead, showing what the result will be after the next package.
    /// </summary>
    private static readonly HashSet<string> ClearedDefaults = [];

    /// <summary>
    /// Keys saved with Save Default this session. They show the new default over any personal roll,
    /// so the save is visible without writing a personal file. Save Default becomes the "last known
    /// version the user edited" and thus should be what they see. Save or Clear hands the key back.
    /// </summary>
    private static readonly HashSet<string> SavedDefaults = [];

    /// <summary>
    /// Adds a <c>res://</c> folder of <c>{key}.hsv</c> rolls. Registered folders are searched in
    /// order, before the ones found automatically, and the first match wins. The export preset's
    /// include_filter needs <c>*.hsv</c>, or Godot leaves the files out of the <c>.pck</c>.
    /// </summary>
    public static void RegisterDefaultsDirectory(string resDirectory)
    {
        AddUnique(DefaultsDirectories, resDirectory);
        ClearDefaultsCache();
    }

    /// <summary>Forgets every cached default, for when the folders they come from change.</summary>
    internal static void ClearDefaultsCache() => DefaultsCache.Clear();

    /// <summary>Adds a <c>res://</c> folder the portrait picker searches recursively for <c>big/*.png</c>.</summary>
    public static void RegisterPortraitDirectory(string resDirectory) =>
        AddUnique(PortraitDirectories, resDirectory);

    internal static IEnumerable<string> GetDefaultsDirectories()
    {
        LegacyCopies.Discover();
        return DefaultsDirectories.Concat(LegacyCopies.DefaultsDirectories).Distinct();
    }

    internal static IEnumerable<string> GetPortraitDirectories()
    {
        LegacyCopies.Discover();
        return PortraitDirectories.Concat(LegacyCopies.PortraitDirectories).Distinct();
    }

    private static void AddUnique(List<string> list, string directory)
    {
        directory = directory.TrimEnd('/');
        if (!list.Contains(directory)) list.Add(directory);
    }

    internal static void LoadUserRolls()
    {
        string directory = ProjectSettings.GlobalizePath(SaveDirectory);
        Directory.CreateDirectory(directory);
        LoadRollsFrom(directory, CardHsvModifiers);
        LoadConfig();
    }

    /// <summary>
    /// Reads every <c>.hsv</c> in a filesystem folder into <paramref name="into"/>. Keys already
    /// present are kept, so whichever folder is loaded first wins.
    /// </summary>
    internal static void LoadRollsFrom(string directory, Dictionary<string, CardHsvData> into)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var path in Directory.GetFiles(directory, "*.hsv"))
        {
            try
            {
                var hsvData = JsonSerializer.Deserialize<CardHsvData>(File.ReadAllText(path));
                if (hsvData != null && !string.IsNullOrEmpty(hsvData.CardId) && into.TryAdd(hsvData.CardId, hsvData))
                    MainFile.Logger.Info($"Loaded data for: {hsvData.CardId}");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"Failed to load '{path}': {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A personal roll: one saved from this editor, else one saved by a pasted copy's editor.
    /// </summary>
    public static CardHsvData? GetCardData(string cardId)
    {
        if (CardHsvModifiers.TryGetValue(cardId, out var data)) return data;

        LegacyCopies.Discover();
        return LegacyCopies.UserRolls.GetValueOrDefault(cardId);
    }

    /// <summary>
    /// The roll to render <paramref name="card"/> with: a scoped key first for a reprint (see
    /// <see cref="ArtContext"/>), then the plain key, each checking personal rolls before defaults.
    /// The portrait override and the color grading both come through here, so they never disagree.
    /// </summary>
    public static CardHsvData? Resolve(CardModel? card)
    {
        if (card == null) return null;

        string cardId = card.Id.ToString();

        var character = ArtContext.For(card);
        if (character != null && ArtContext.NeedsScoping(card, character))
        {
            string scoped = ArtContext.ScopedKey(cardId, character);
            var scopedData = GetPersonalRoll(scoped) ?? GetDefaultHsvForCard(scoped);
            if (scopedData != null) return scopedData;
        }

        return GetPersonalRoll(cardId) ?? GetDefaultHsvForCard(cardId);
    }

    /// <summary>
    /// The personal roll to render with, or null while the player has personal edits turned off or
    /// a default was just saved over it. They stay loaded either way, so turning the setting back on
    /// needs no restart.
    /// </summary>
    private static CardHsvData? GetPersonalRoll(string key) =>
        ArtRollerConfig.ApplyPersonalEdits && !SavedDefaults.Contains(key) ? GetCardData(key) : null;

    /// <summary>
    /// The default shipped for <paramref name="cardId"/>, or null. The result is cached and shared
    /// between callers, so copy it with <c>with { }</c> before changing it.
    /// </summary>
    public static CardHsvData? GetDefaultHsvForCard(string cardId)
    {
        LegacyCopies.Discover();
        if (DefaultsCache.TryGetValue(cardId, out var cached)) return cached;

        var data = LoadDefault(cardId);
        DefaultsCache[cardId] = data;
        return data;
    }

    private static CardHsvData? LoadDefault(string cardId)
    {
        // Save Default's output folder comes first, so a freshly saved default shows without a rebuild.
        string outputPath = GetDefaultOutputPath(cardId);
        if (File.Exists(outputPath))
        {
            try
            {
                return JsonSerializer.Deserialize<CardHsvData>(File.ReadAllText(outputPath));
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"Failed to load user default for '{cardId}': {ex.Message}");
            }
        }

        if (ClearedDefaults.Contains(cardId)) return null;

        foreach (var directory in GetDefaultsDirectories())
        {
            string resPath = $"{directory}/{cardId}.hsv";
            if (!Godot.FileAccess.FileExists(resPath)) continue;

            try
            {
                using var file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
                if (file == null)
                {
                    MainFile.Logger.Error($"Could not open '{resPath}': {Godot.FileAccess.GetOpenError()}");
                    continue;
                }

                return JsonSerializer.Deserialize<CardHsvData>(file.GetAsText());
            }
            catch (Exception ex)
            {
                MainFile.Logger.Error($"Failed to load default data for '{cardId}': {ex.Message}");
            }
        }

        return null;
    }

    public static void SaveHsvForCard(string cardId, CardHsvData data)
    {
        data = Normalize(cardId, data);
        CardHsvModifiers[cardId] = data;
        SavedDefaults.Remove(cardId);
        SaveToFile(data, GetUserPath(cardId));
    }

    public static void DeleteHsvForCard(string cardId)
    {
        CardHsvModifiers.Remove(cardId);
        SavedDefaults.Remove(cardId);
        try
        {
            string path = GetUserPath(cardId);
            if (File.Exists(path))
            {
                File.Delete(path);
                MainFile.Logger.Info($"Deleted data for {cardId}");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to delete data for '{cardId}': {ex.Message}");
        }
    }

    public static void SaveDefaultHsvForCard(string cardId, CardHsvData data)
    {
        SaveToFile(Normalize(cardId, data), GetDefaultOutputPath(cardId));
        ClearedDefaults.Remove(cardId);
        SavedDefaults.Add(cardId);
        DefaultsCache.Remove(cardId);
    }

    private static CardHsvData Normalize(string cardId, CardHsvData data) => data with
    {
        CardId = cardId,
        PortraitPath = string.IsNullOrWhiteSpace(data.PortraitPath) ? null : data.PortraitPath,
    };

    /// <summary>
    /// Deletes the default written by <see cref="SaveDefaultHsvForCard"/>, and hides any packed copy
    /// for the rest of the session (see <see cref="ClearedDefaults"/>).
    /// </summary>
    public static void DeleteDefaultHsvForCard(string cardId)
    {
        ClearedDefaults.Add(cardId);
        SavedDefaults.Remove(cardId);
        DefaultsCache.Remove(cardId);
        try
        {
            string path = GetDefaultOutputPath(cardId);
            if (File.Exists(path))
            {
                File.Delete(path);
                MainFile.Logger.Info($"Deleted default for {cardId} at {path}");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to delete default for '{cardId}': {ex.Message}");
        }
    }

    public static void SaveConfig()
    {
        try
        {
            var config = new { defaults_output_directory = DefaultsOutputDirectory };
            string path = GetConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"Config saved to {path}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to save config: {ex.Message}");
        }
    }

    public static void LoadConfig()
    {
        try
        {
            string path = GetConfigPath();
            if (!File.Exists(path)) return;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("defaults_output_directory", out var prop))
                DefaultsOutputDirectory = prop.GetString() ?? "";

            MainFile.Logger.Info($"Config loaded. DefaultsOutputDirectory='{DefaultsOutputDirectory}'");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load config: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static string GetUserPath(string cardId) =>
        Path.Combine(ProjectSettings.GlobalizePath(SaveDirectory), $"{cardId}.hsv");

    private static string GetConfigPath() =>
        Path.Combine(ProjectSettings.GlobalizePath(SaveDirectory), ConfigFileName);

    private static void SaveToFile(CardHsvData data, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            MainFile.Logger.Info($"Saved data for {data.CardId} to {path}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to save data for '{data.CardId}': {ex.Message}");
        }
    }

    private static string GetDefaultOutputPath(string cardId)
    {
        string dir = !string.IsNullOrEmpty(DefaultsOutputDirectory)
            ? DefaultsOutputDirectory
            : Path.Combine(ProjectSettings.GlobalizePath(SaveDirectory), "defaults");
        return Path.Combine(dir, $"{cardId}.hsv");
    }
}

