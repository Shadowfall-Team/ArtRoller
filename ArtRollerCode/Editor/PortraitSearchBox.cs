using Godot;
using System;
using System.Collections.Generic;
using ArtRoller.Patches;

namespace ArtRoller.Editor;

public class PortraitSearchBox
{
    private readonly LineEdit _searchBox;
    private readonly ItemList _searchList;
    /// <summary>What the box showed before the current search, restored when the search is cancelled.</summary>
    private string _committedText = "";

    public event Action<string>? PortraitSelected;

    public string Text
    {
        get => _searchBox.Text;
        set
        {
            _committedText = value;
            _searchBox.Text = value;
            _searchList.Hide();
        }
    }

    public PortraitSearchBox(Godot.Node parent)
    {
        _searchBox = new LineEdit();
        _searchBox.PlaceholderText = "Search portrait paths...";
        EditorHoverTip.Attach(_searchBox, "PORTRAIT");
        _searchBox.CustomMinimumSize = new Vector2(300, 40);
        _searchBox.TextChanged += OnSearchTextChanged;
        parent.AddChild(_searchBox);

        _searchList = new ItemList();
        _searchList.ItemSelected += OnItemSelected;
        _searchList.TopLevel = true;
        _searchList.Hide();

        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        bgStyle.BorderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        bgStyle.SetBorderWidthAll(1);
        _searchList.AddThemeStyleboxOverride("panel", bgStyle);

        _searchBox.AddChild(_searchList);
        _searchBox.AddChild(new SearchDismissWatcher(_searchList, _searchBox, Cancel));

        // A search typed while the list is still building shows a placeholder; redo it once ready.
        Preload();
        if (!_portraits!.IsCompleted)
            _portraits.ContinueWith(_ => Callable.From(OnPortraitsReady).CallDeferred());
    }

    private void OnPortraitsReady()
    {
        if (GodotObject.IsInstanceValid(_searchBox) && _searchList.Visible)
            OnSearchTextChanged(_searchBox.Text);
    }

    private void OnSearchTextChanged(string searchText)
    {
        _searchList.Clear();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            _searchList.Hide();
            return;
        }

        if (!_portraits!.IsCompletedSuccessfully)
        {
            _searchList.AddItem("Loading portraits...", selectable: false);
            ShowResults();
            return;
        }

        int count = 0;
        foreach (string path in _portraits.Result)
        {
            string display = BuildDisplayName(path);
            
            if (display.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                _searchList.AddItem(display);
                _searchList.SetItemMetadata(count, path);
                count++;
                if (count >= 15) break;
            }
        }

        if (count > 0)
            ShowResults();
        else
            _searchList.Hide();
    }

    private void ShowResults()
    {
        Vector2 globalPos = _searchBox.GlobalPosition;
        _searchList.GlobalPosition = new Vector2(globalPos.X, globalPos.Y - 250);
        _searchList.Size = new Vector2(_searchBox.Size.X, 250);
        _searchList.Show();
    }

    private void OnItemSelected(long index)
    {
        string selectedPath = (string)_searchList.GetItemMetadata((int)index);

        string name = System.IO.Path.GetFileNameWithoutExtension(selectedPath);
        string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(selectedPath)) ?? "";
        Text = string.IsNullOrEmpty(folder) ? name : $"{folder}/{name}";
        PortraitSelected?.Invoke(selectedPath);
    }

    /// <summary>Closes the results and puts the box back to how it was, leaving the art unchanged.</summary>
    private void Cancel()
    {
        _searchList.Hide();
        _searchBox.Text = _committedText;
        _searchBox.ReleaseFocus();
    }

    /// <summary>
    /// "folder/name" for the picker list. Custom art lives one level deeper than base game art
    /// (<c>card_portraits/ironclad/big/foo.png</c>), so naming the immediate folder would label
    /// every custom portrait "big/" and lose the character it belongs to. Step over "big" so the
    /// character folder is shown instead.
    /// </summary>
    private static string BuildDisplayName(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        string? folderPath = System.IO.Path.GetDirectoryName(path);
        string folder = System.IO.Path.GetFileName(folderPath) ?? "";

        if (folder.Equals("big", StringComparison.OrdinalIgnoreCase))
            folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(folderPath)) ?? folder;

        return string.IsNullOrEmpty(folder) ? name : $"{folder}/{name}";
    }

    /// <summary>Every portrait the search offers, built once per session on a background thread.</summary>
    private static Task<List<string>>? _portraits;

    /// <summary>
    /// Starts building the portrait list, if it has not been started. The file checks and folder
    /// scans run in the background, since doing them when the editor first opened caused a hitch.
    /// </summary>
    internal static void Preload()
    {
        if (_portraits != null) return;

        // The game's card and mod lists are not thread-safe, so read them here on the main thread.
        var cardPaths = new List<string>();
        List<string> roots = [];
        try
        {
            // Each card's own art: its PortraitPath would return a roll's replacement instead,
            // hiding the original of every card that has one.
            foreach (var card in MegaCrit.Sts2.Core.Models.ModelDb.AllCards)
                cardPaths.Add(CardModelPortraitPatch.OriginalPortraitPath(card));
            roots = CardArtRoller.GetPortraitDirectories().ToList();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to list card portraits: {ex.Message}");
        }

        _portraits = Task.Run(() => LoadAllPortraitPaths(cardPaths, roots));
    }

    private static List<string> LoadAllPortraitPaths(List<string> cardPaths, List<string> roots)
    {
        var paths = new List<string>();
        var seen = new HashSet<string>();
        try
        {
            foreach (string path in cardPaths)
            {
                if (!string.IsNullOrWhiteSpace(path) && !seen.Contains(path) && ResourceLoader.Exists(path))
                {
                    seen.Add(path);
                    paths.Add(path);
                }
            }

            // Card art only covers art some card uses, so also scan the mods' portrait folders for
            // art whose card was removed or has not been assigned yet.
            int beforeCustom = paths.Count;
            foreach (var root in roots)
                AddPortraitsUnder(root, paths, seen);
            MainFile.Logger.Info($"Loaded {paths.Count} portrait paths, {paths.Count - beforeCustom} of them from mod folders.");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"Failed to load portrait paths: {ex.Message}");
        }
        return paths;
    }

    /// <summary>
    /// Recursively collects usable <c>big/</c> portraits under <paramref name="directory"/>.
    /// Only the big variant is collected: it is what a card actually renders, and including the
    /// small siblings would fill the picker with same-named duplicates at the wrong resolution.
    /// </summary>
    private static void AddPortraitsUnder(string directory, List<string> paths, HashSet<string> seen)
    {
        using var dir = DirAccess.Open(directory);
        if (dir == null) return;

        dir.ListDirBegin();
        while (true)
        {
            string entry = dir.GetNext();
            if (string.IsNullOrEmpty(entry)) break;
            if (entry is "." or "..") continue;

            string full = $"{directory.TrimEnd('/')}/{entry}";

            if (dir.CurrentIsDir())
            {
                AddPortraitsUnder(full, paths, seen);
                continue;
            }

            // Exported builds surface imported textures as .import / .remap siblings rather than
            // the source file, so strip that suffix before testing the resource path.
            if (full.EndsWith(".import", StringComparison.OrdinalIgnoreCase) ||
                full.EndsWith(".remap", StringComparison.OrdinalIgnoreCase))
            {
                full = full[..full.LastIndexOf('.')];
            }

            if (!full.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (!full.Contains("/big/", StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Contains(full)) continue;
            if (!ResourceLoader.Exists(full)) continue;

            seen.Add(full);
            paths.Add(full);
        }
        dir.ListDirEnd();
    }
}