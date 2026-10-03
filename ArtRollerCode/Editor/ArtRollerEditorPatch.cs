using HarmonyLib;
using Godot;
using ArtRoller.Patches;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace ArtRoller.Editor;

[HarmonyPatch(typeof(NCardLibrary))]
public class ArtRollerEditorPatch
{
    private const string SliderScenePath = "res://scenes/screens/settings_slider.tscn";

    private const float ScreenMargin = 24f;
    private const float ValueReadoutWidth = 85f;
    private const float RowLabelWidth = 120f;

    /// <summary>
    /// The game's slider scene is about 64px tall, mostly empty padding around the grabber. Pulling
    /// rows together by this much is what lets all of them fit on a 1080-tall screen.
    /// </summary>
    private const int SliderRowSeparation = -10;

    /// <summary>One slider: how it reads and writes the roll, and its range in slider units (value x 100).</summary>
    private sealed record SliderSpec(
        string Label, string HoverTipKey, double Min, double Max,
        Func<CardHsvData, float> Get, Action<CardHsvData, float> Set);

    // The Standard tab's sections, top to bottom. Tint sits above them and the flips below; the
    // gaps between sections are what Reposition stretches to fill the panel.
    private static readonly SliderSpec[][] StandardSections =
    [
        [
            new("Hue",          "HUE",          0, 100, d => d.Hue,         (d, v) => d.Hue = v),
            new("Sat",          "SATURATION",   0, 200, d => d.Saturation,  (d, v) => d.Saturation = v),
            new("Lum",          "LUMINANCE",   50, 150, d => d.Value,       (d, v) => d.Value = v),
            new("Gamma",        "GAMMA",       50, 150, d => d.Gamma,       (d, v) => d.Gamma = v),
        ],
        [
            new("Red",          "RED",          0, 200, d => d.Red,         (d, v) => d.Red = v),
            new("Red Offset",   "RED_OFFSET",  50, 150, d => d.RedOffset,   (d, v) => d.RedOffset = v),
            new("Green",        "GREEN",        0, 200, d => d.Green,       (d, v) => d.Green = v),
            new("Green Offset", "GREEN_OFFSET",50, 150, d => d.GreenOffset, (d, v) => d.GreenOffset = v),
            new("Blue",         "BLUE",         0, 200, d => d.Blue,        (d, v) => d.Blue = v),
            new("Blue Offset",  "BLUE_OFFSET", 50, 150, d => d.BlueOffset,  (d, v) => d.BlueOffset = v),
        ],
        [
            new("Contrast",     "CONTRAST",    50, 200, d => d.Contrast,    (d, v) => d.Contrast = v),
        ],
    ];

    private const int TabSectionSeparation = 6;

    private static SliderSpec[] SelectiveSliders(int i) =>
    [
        new("Width",       "SELECTIVE_WIDTH",      0, 100, d => d.GetSelective(i).Width,      (d, v) => d.SetSelective(i, d.GetSelective(i) with { Width = v })),
        new("Shift",       "SELECTIVE_SHIFT",      0, 200, d => d.GetSelective(i).Shift,      (d, v) => d.SetSelective(i, d.GetSelective(i) with { Shift = v })),
        new("Saturation",  "SELECTIVE_SATURATION", 0, 200, d => d.GetSelective(i).Saturation, (d, v) => d.SetSelective(i, d.GetSelective(i) with { Saturation = v })),
        new("Brightness",  "SELECTIVE_BRIGHTNESS", 0, 200, d => d.GetSelective(i).Brightness, (d, v) => d.SetSelective(i, d.GetSelective(i) with { Brightness = v })),
        new("Softness",    "SELECTIVE_SOFTNESS",   0, 100, d => d.GetSelective(i).Softness,   (d, v) => d.SetSelective(i, d.GetSelective(i) with { Softness = v })),
    ];

    private enum Tab { Standard, Selective }

    /// <summary>Kept across cards, so working through a set of cards on one tab stays on it.</summary>
    private static Tab _tab = Tab.Standard;

    // ── UI refs ───────────────────────────────────────────────────────────
    private static PanelContainer? _sliderContainer;
    private static readonly List<(SliderSpec Spec, Godot.Range Slider)> _sliderControls = [];
    private static ColorPickerButton? _tintPicker;
    private static readonly List<(int Set, ColorPickerButton Picker)> _selectivePickers = [];
    private static VBoxContainer? _tabHost;
    private static VBoxContainer? _standardTab;
    private static VBoxContainer? _selectiveTab;
    private static Button? _standardTabButton;
    private static Button? _selectiveTabButton;
    private static Button? _flipXButton;
    private static Button? _flipYButton;
    private static Button? _pasteButton;
    private static PortraitSearchBox? _portraitSearch;
    private static VBoxContainer? _devControls;
    private static Label? _keyLabel;
    private static LineEdit? _defaultOutputFolderField;
    private static FileDialog? _folderDialog;

    // ── State ─────────────────────────────────────────────────────────────
    private static CardModel? _currentCard;
    private static NCardHolder? _libraryHolder;
    private static bool _openedFromLibrary;

    /// <summary>What the controls show and the preview renders. Written to disk only by Save.</summary>
    private static CardHsvData _state = new();
    private static CardHsvData _savedState = new();
    private static CardHsvData? _clipboard;

    /// <summary>Set while controls are being filled from <see cref="_state"/>, so they do not write it back.</summary>
    private static bool _syncing;

    // ── Setup ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Built on first use rather than when the library opens: the portrait picker probes every
    /// card's art, which is wasted work for the players who never turn the editor on.
    /// </summary>
    private static void EnsureBuilt()
    {
        if (_sliderContainer != null && GodotObject.IsInstanceValid(_sliderContainer)) return;
        var inspectScreen = NGame.Instance!.GetInspectCardScreen();
        _sliderControls.Clear();
        _selectivePickers.Clear();

        // Sized to its contents and placed by Reposition, so the panel never runs off screen.
        _sliderContainer = new PanelContainer();
        _sliderContainer.Visible = false;
        _sliderContainer.AddThemeStyleboxOverride("panel", CreatePanelStyle());

        // The inspect screen closes on any click reaching its full-screen backstop. Stopping the
        // mouse across the whole panel keeps misclicks between controls from closing it.
        _sliderContainer.MouseFilter = Control.MouseFilterEnum.Stop;

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        _sliderContainer.AddChild(vbox);

        PackedScene sliderScene = GD.Load<PackedScene>(SliderScenePath);

        // Tabs swap everything above the portrait search; the search and buttons below are shared.
        var tabRow = AddRow(vbox);
        var tabGroup = new ButtonGroup();
        _standardTabButton  = AddTabButton(tabRow, "Standard",      "TAB_STANDARD",  tabGroup, Tab.Standard);
        _selectiveTabButton = AddTabButton(tabRow, "Selective Hue", "TAB_SELECTIVE", tabGroup, Tab.Selective);
        vbox.AddChild(new HSeparator());

        _tabHost = new VBoxContainer();
        vbox.AddChild(_tabHost);

        _standardTab = new VBoxContainer();
        _standardTab.AddThemeConstantOverride("separation", TabSectionSeparation);
        _tabHost.AddChild(_standardTab);

        _tintPicker = AddColorRow(_standardTab, "Tint", "TINT", color => _state.Tint = ToHex(color));

        foreach (var section in StandardSections)
        {
            var sectionBox = new VBoxContainer();
            sectionBox.AddThemeConstantOverride("separation", SliderRowSeparation);
            _standardTab.AddChild(sectionBox);
            foreach (var spec in section)
                AddSliderRow(sectionBox, sliderScene, spec);
        }

        var flipRow = AddRow(_standardTab);
        _flipXButton = AddButton(flipRow, "Flip X: OFF", 130, "FLIP_X", OnFlipXPressed);
        _flipYButton = AddButton(flipRow, "Flip Y: OFF", 130, "FLIP_Y", OnFlipYPressed);

        _selectiveTab = new VBoxContainer();
        _selectiveTab.AddThemeConstantOverride("separation", 6);
        _tabHost.AddChild(_selectiveTab);

        for (int i = 0; i < CardHsvData.SelectiveSetCount; i++)
        {
            if (i > 0) _selectiveTab.AddChild(new HSeparator());

            int set = i;
            var picker = AddColorRow(_selectiveTab, $"Target {i + 1}", "SELECTIVE_TARGET",
                color => _state.SetSelective(set, _state.GetSelective(set) with { Color = ToHex(color) }),
                onReset: () =>
                {
                    _state.SetSelective(set, new SelectiveHue());
                    ShowState();
                });
            _selectivePickers.Add((set, picker));

            var setBox = new VBoxContainer();
            setBox.AddThemeConstantOverride("separation", SliderRowSeparation);
            _selectiveTab.AddChild(setBox);
            foreach (var spec in SelectiveSliders(set))
                AddSliderRow(setBox, sliderScene, spec);
        }

        vbox.AddChild(new HSeparator());

        _portraitSearch = new PortraitSearchBox(vbox);
        _portraitSearch.PortraitSelected += OnPortraitSelected;

        BuildButtonRows(vbox);

        inspectScreen.AddChild(_sliderContainer);
        inspectScreen.MoveChild(_sliderContainer, inspectScreen.GetChildCount() - 1);

        _defaultOutputFolderField?.Text = CardArtRoller.DefaultsOutputDirectory;
        ShowTab(_tab);
    }

    private static Button AddTabButton(HBoxContainer row, string text, string hoverTipKey, ButtonGroup group, Tab tab)
    {
        var button = AddButton(row, text, 190, hoverTipKey, () => ShowTab(tab));
        button.ToggleMode = true;
        button.ButtonGroup = group;
        return button;
    }

    private static void ShowTab(Tab tab)
    {
        _tab = tab;
        _standardTab?.Visible = tab == Tab.Standard;
        _selectiveTab?.Visible = tab == Tab.Selective;
        _standardTabButton?.SetPressedNoSignal(tab == Tab.Standard);
        _selectiveTabButton?.SetPressedNoSignal(tab == Tab.Selective);

        // Normally a no-op, since Reposition reserves the taller tab's height; this covers the case
        // where the hidden tab had not been measured yet.
        Callable.From(Reposition).CallDeferred();
    }

    /// <summary>
    /// Sizes and centres the panel, scaling it down only if it still would not fit. Called deferred,
    /// so the containers have measured their children.
    /// </summary>
    private static void Reposition()
    {
        if (_sliderContainer == null || !GodotObject.IsInstanceValid(_sliderContainer)) return;

        // Reserve the taller tab's height, so switching tabs never resizes or rescales the panel, and
        // spread the Standard tab's sections apart to fill it rather than leave a gap at the bottom.
        if (_tabHost != null && _standardTab != null && _selectiveTab != null)
        {
            _standardTab.AddThemeConstantOverride("separation", TabSectionSeparation);
            float standard = _standardTab.GetCombinedMinimumSize().Y;
            float selective = _selectiveTab.GetCombinedMinimumSize().Y;

            int gaps = _standardTab.GetChildCount() - 1;
            if (selective > standard && gaps > 0)
                _standardTab.AddThemeConstantOverride("separation",
                    TabSectionSeparation + Mathf.FloorToInt((selective - standard) / gaps));

            _tabHost.CustomMinimumSize = new Vector2(0, Mathf.Max(standard, selective));
        }

        _sliderContainer.Scale = Vector2.One;
        _sliderContainer.ResetSize();

        var screen = _sliderContainer.GetViewportRect().Size;
        float available = screen.Y - 2 * ScreenMargin;
        float scale = Mathf.Min(1f, available / _sliderContainer.Size.Y);
        _sliderContainer.Scale = Vector2.One * scale;

        float y = Mathf.Max(ScreenMargin, (screen.Y - _sliderContainer.Size.Y * scale) / 2f);
        _sliderContainer.Position = new Vector2(ScreenMargin, y);
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.07f, 0.82f),
            BorderColor = new Color(1f, 1f, 1f, 0.15f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(12);
        style.SetContentMarginAll(20);
        return style;
    }

    private static void BuildButtonRows(VBoxContainer vbox)
    {
        var buttonRow = AddRow(vbox);
        AddButton(buttonRow, "Save",  78, "SAVE",  OnSaveButtonPressed);
        AddButton(buttonRow, "Clear", 78, "CLEAR", OnClearButtonPressed);
        AddButton(buttonRow, "Copy",  78, "COPY",  OnCopyPressed);
        _pasteButton = AddButton(buttonRow, "Paste", 78, "PASTE", OnPastePressed);
        _pasteButton.Disabled = true;
        AddButton(buttonRow, "Reset", 78, "RESET", OnResetPressed);

        // Developer Mode only. Kept in one container so a config change shows or hides it on the
        // next inspect.
        _devControls = new VBoxContainer();
        _devControls.AddThemeConstantOverride("separation", 6);
        vbox.AddChild(_devControls);

        var defaultRow = AddRow(_devControls);
        AddButton(defaultRow, "Save Default",  170, "SAVE_DEFAULT",  OnSaveDefaultButtonPressed);
        AddButton(defaultRow, "Clear Default", 170, "CLEAR_DEFAULT", OnClearDefaultButtonPressed);

        var folderRow = AddRow(_devControls);

        _defaultOutputFolderField = new LineEdit();
        _defaultOutputFolderField.CustomMinimumSize = new Vector2(300, 0);
        _defaultOutputFolderField.PlaceholderText = $"{CardArtRoller.SaveDirectory}/defaults";
        _defaultOutputFolderField.Editable = false;
        EditorHoverTip.Attach(_defaultOutputFolderField, "OUTPUT_FOLDER");
        folderRow.AddChild(_defaultOutputFolderField);

        var browseBtn = new Button();
        browseBtn.Text = "...";
        EditorHoverTip.Attach(browseBtn, "BROWSE");
        browseBtn.CustomMinimumSize = new Vector2(40, 0);
        browseBtn.Pressed += OnBrowseFolderPressed;
        folderRow.AddChild(browseBtn);

        _keyLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        EditorHoverTip.Attach(_keyLabel, "KEY");
        _devControls.AddChild(_keyLabel);
    }

    private static void OnBrowseFolderPressed()
    {
        if (_folderDialog == null)
        {
            _folderDialog = new FileDialog();
            _folderDialog.FileMode = FileDialog.FileModeEnum.OpenDir;
            _folderDialog.Access = FileDialog.AccessEnum.Filesystem;
            _folderDialog.Title = "Select Default Output Folder";
            _folderDialog.DirSelected += OnFolderSelected;
            _sliderContainer!.AddChild(_folderDialog);
        }

        _folderDialog.PopupCentered(new Vector2I(600, 400));
    }

    private static void OnFolderSelected(string path)
    {
        if (_defaultOutputFolderField == null) return;

        CardArtRoller.DefaultsOutputDirectory = path;
        _defaultOutputFolderField.Text = path;
        CardArtRoller.SaveConfig();
    }

    // ── Control builders ──────────────────────────────────────────────────

    private static HBoxContainer AddRow(Node parent)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        row.Alignment = BoxContainer.AlignmentMode.Center;
        parent.AddChild(row);
        return row;
    }

    private static Button AddButton(Node parent, string text, int width, string hoverTipKey, Action onPressed)
    {
        var btn = new Button();
        btn.Text = text;
        EditorHoverTip.Attach(btn, hoverTipKey);
        btn.CustomMinimumSize = new Vector2(width, 36);
        btn.Pressed += onPressed;
        parent.AddChild(btn);
        return btn;
    }

    private static Label AddRowLabel(HBoxContainer row, string text, string hoverTipKey)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(RowLabelWidth, 0),
        };
        EditorHoverTip.Attach(label, hoverTipKey);
        row.AddChild(label);
        return label;
    }

    private static ColorPickerButton AddColorRow(
        VBoxContainer parent, string labelText, string hoverTipKey, Action<Color> onChanged, Action? onReset = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 15);
        row.Alignment = BoxContainer.AlignmentMode.End;
        parent.AddChild(row);

        // Sits in the column the slider rows below keep free for their value readouts.
        if (onReset != null)
        {
            var reset = AddButton(row, "Reset", (int)ValueReadoutWidth, "SELECTIVE_RESET", onReset);
            reset.CustomMinimumSize = new Vector2(ValueReadoutWidth, 30);
            reset.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        }

        var picker = new ColorPickerButton
        {
            CustomMinimumSize = new Vector2(200, 36),
            EditAlpha = false,
        };
        // The eyedropper is how a selective target gets picked straight off the card's art.
        picker.GetPicker().SamplerVisible = true;
        // Same thin frame BaseLib gives its config color pickers, so the swatch reads as a control.
        var frame = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color(0.3f, 0.3f, 0.3f) };
        frame.SetBorderWidthAll(2);
        frame.SetContentMarginAll(2);
        foreach (var state in new[] { "normal", "pressed", "hover", "focus" })
            picker.AddThemeStyleboxOverride(state, frame);

        picker.ColorChanged += color =>
        {
            if (_syncing) return;
            onChanged(color);
            UpdateCardShader();
        };
        EditorHoverTip.Attach(picker, hoverTipKey);
        row.AddChild(picker);

        row.AddChild(new Control { CustomMinimumSize = new Vector2(15, 0) });
        AddRowLabel(row, labelText, hoverTipKey);
        return picker;
    }

    private static void AddSliderRow(VBoxContainer parent, PackedScene scene, SliderSpec spec)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 15);
        row.Alignment = BoxContainer.AlignmentMode.End;
        parent.AddChild(row);

        // The settings slider scene draws its value readout about 93px left of its own rect, so
        // without this the readout hangs outside the panel. Measured from a 1920x1080 screenshot.
        row.AddChild(new Control { CustomMinimumSize = new Vector2(ValueReadoutWidth, 0) });

        var sliderInstance = (Control)scene.Instantiate();
        row.AddChild(sliderInstance);
        sliderInstance.CustomMinimumSize = new Vector2(200, sliderInstance.CustomMinimumSize.Y);

        if (sliderInstance.GetNodeOrNull<Godot.Range>("Slider") is { } slider)
        {
            // The game's NSlider places its handle at Value / MaxValue and maps a click to
            // position x MaxValue, so it only draws correctly for ranges starting at 0. It runs
            // 0..span and the spec's minimum is added back here; see ToSlider and FromSlider.
            slider.MinValue = 0;
            slider.MaxValue = spec.Max - spec.Min;
            slider.CustomMinimumSize = new Vector2(10, slider.CustomMinimumSize.Y);

            // Connected after the scene's own handler, so this readout wins over its "N%" text.
            var readout = sliderInstance.GetNodeOrNull<MegaLabel>("SliderValue");
            readout?.SetTextAutoSize((slider.Value + spec.Min).ToString("0.0"));
            slider.ValueChanged += val =>
            {
                readout?.SetTextAutoSize((val + spec.Min).ToString("0.0"));
                if (_syncing) return;
                spec.Set(_state, FromSlider(spec, val));
                UpdateCardShader();
            };

            EditorHoverTip.Attach(slider, spec.HoverTipKey);
            _sliderControls.Add((spec, slider));
        }

        row.AddChild(new Control { CustomMinimumSize = new Vector2(15, 0) });
        AddRowLabel(row, spec.Label, spec.HoverTipKey);
    }

    private static string ToHex(Color color) => "#" + color.ToHtml(false);

    /// <summary>A stored roll value (1 = unchanged) as a position on a 0-based game slider.</summary>
    private static double ToSlider(SliderSpec spec, float stored) => stored * 100.0 - spec.Min;

    private static float FromSlider(SliderSpec spec, double position) => (float)((position + spec.Min) / 100.0);

    // ── Show card ─────────────────────────────────────────────────────────

    private static NCardHolder? _pendingLibraryHolder;

    /// <summary>
    /// Notes that the next inspect-screen Open comes from the Card Library. The same screen serves
    /// the deck and other views, which should never grow an editor. A prefix, because the library
    /// opens the screen from inside this method.
    /// </summary>
    /// <summary>Builds the portrait search list while the player browses, before any card is opened.</summary>
    [HarmonyPatch(nameof(NCardLibrary._Ready))]
    [HarmonyPostfix]
    static void PreloadPortraits()
    {
        if (ArtRollerConfig.EditorEnabled) PortraitSearchBox.Preload();
    }

    [HarmonyPatch("ShowCardDetail")]
    [HarmonyPrefix]
    // ReSharper disable once UnusedMember.Local
    static void MarkOpenedFromLibrary(NCardHolder holder) => _pendingLibraryHolder = holder;

    /// <summary>
    /// Every Open decides afresh whether this viewing is the library's. Do not clear it on Close:
    /// the screen is created inside the first ShowCardDetail and calls Close from its own _Ready,
    /// which hid the editor until the second click.
    /// </summary>
    internal static void OnInspectScreenOpening()
    {
        _openedFromLibrary = _pendingLibraryHolder != null;
        _libraryHolder = _pendingLibraryHolder;
        _pendingLibraryHolder = null;
    }

    /// <summary>
    /// Driven by the inspect screen rather than the library, because its arrow buttons change card
    /// without going back through the library.
    /// </summary>
    internal static void OnInspectedCardChanged(CardModel card)
    {
        _currentCard = card;

        // Checked on every card so the config toggles take effect without a restart.
        if (!_openedFromLibrary || !ArtRollerConfig.EditorEnabled
            || !SaveManager.Instance.Progress.DiscoveredCards.Contains(card.Id))
        {
            _sliderContainer?.Visible = false;
            return;
        }

        EnsureBuilt();
        if (_sliderContainer == null) return;

        _devControls?.Visible = ArtRollerConfig.DeveloperMode;
        _keyLabel?.Text = $"Key: {ArtContext.KeyFor(card)}";

        _sliderContainer.Visible = true;
        var parent = _sliderContainer.GetParent();
        parent.MoveChild(_sliderContainer, parent.GetChildCount() - 1);
        Callable.From(Reposition).CallDeferred();

        LoadStateFromRoll();
    }

    /// <summary>
    /// The inspect screen rebuilds its card when the upgrade preview is toggled, and the rebuild
    /// renders the saved roll. Put the unsaved preview back on top.
    /// </summary>
    internal static void OnInspectedCardRedrawn(CardModel card)
    {
        if (!IsEditing || card != _currentCard) return;
        RefreshPortraitTexture();
        UpdateCardShader();
    }

    private static bool IsEditing =>
        _sliderContainer != null && GodotObject.IsInstanceValid(_sliderContainer)
        && _sliderContainer.IsVisibleInTree() && _currentCard != null;

    /// <summary>Loads whatever actually renders for the card, which is what Resolve returns.</summary>
    private static void LoadStateFromRoll()
    {
        var roll = _currentCard != null ? CardArtRoller.Resolve(_currentCard) : null;
        _state = roll != null ? roll with { } : new CardHsvData();
        ShowState();
        MarkClean();
    }

    private static void ShowState()
    {
        _syncing = true;
        try
        {
            foreach (var (spec, slider) in _sliderControls)
                slider.Value = ToSlider(spec, spec.Get(_state));

            _tintPicker?.Color = CardShaderHelper.ParseColor(_state.Tint, Colors.White);
            foreach (var (set, picker) in _selectivePickers)
                picker.Color = CardShaderHelper.ParseColor(_state.GetSelective(set).Color, Colors.Red);

            _portraitSearch?.Text = !string.IsNullOrEmpty(_state.PortraitPath)
                ? Path.GetFileNameWithoutExtension(_state.PortraitPath)
                : "";
        }
        finally
        {
            _syncing = false;
        }

        // A slider snaps to its step, so read the values back: the state must match what is shown.
        foreach (var (spec, slider) in _sliderControls)
            spec.Set(_state, FromSlider(spec, slider.Value));

        RefreshFlipLabels();
        RefreshPortraitTexture();
        UpdateCardShader();
    }

    private static void RefreshFlipLabels()
    {
        _flipXButton?.Text = _state.FlipH ? "Flip X: ON" : "Flip X: OFF";
        _flipYButton?.Text = _state.FlipV ? "Flip Y: ON" : "Flip Y: OFF";
    }

    private static void RefreshPortraitTexture()
    {
        var portrait = GetInspectedPortrait();
        if (portrait == null || _currentCard == null) return;

        // The card's own art, not the saved roll's override: Reset and a cleared portrait preview it.
        string path = !string.IsNullOrEmpty(_state.PortraitPath) && ResourceLoader.Exists(_state.PortraitPath)
            ? _state.PortraitPath
            : CardModelPortraitPatch.OriginalPortraitPath(_currentCard);

        if (ResourceLoader.Exists(path))
            portrait.Texture = ResourceLoader.Load<Texture2D>(path);
    }

    // ── Unsaved changes ───────────────────────────────────────────────────

    private static void MarkClean() => _savedState = _state with { };

    internal static bool HasUnsavedChanges => IsEditing && _state != _savedState;

    // ── Preview ───────────────────────────────────────────────────────────

    private static void UpdateCardShader()
    {
        if (_currentCard == null) return;
        if (GetInspectedPortrait() is { } portrait)
            CardShaderHelper.ApplyToPortrait(portrait, _state);
    }

    // ── Flips and portrait ────────────────────────────────────────────────

    private static void OnFlipXPressed()
    {
        _state.FlipH = !_state.FlipH;
        RefreshFlipLabels();
        UpdateCardShader();
    }

    private static void OnFlipYPressed()
    {
        _state.FlipV = !_state.FlipV;
        RefreshFlipLabels();
        UpdateCardShader();
    }

    private static void OnPortraitSelected(string path)
    {
        _state.PortraitPath = path;
        RefreshPortraitTexture();
    }

    // ── Buttons ───────────────────────────────────────────────────────────

    private static void OnSaveButtonPressed()
    {
        if (_currentCard == null) return;

        string cardId = ArtContext.KeyFor(_currentCard);
        CardArtRoller.SaveHsvForCard(cardId, _state);
        MarkClean();
        ReloadCards();
        MainFile.Logger.Info($"Saved for card: {cardId}");
    }

    private static void OnClearButtonPressed()
    {
        if (_currentCard == null) return;

        string cardId = ArtContext.KeyFor(_currentCard);
        CardArtRoller.DeleteHsvForCard(cardId);
        ReloadCards();
        LoadStateFromRoll();
        MainFile.Logger.Info($"Cleared for card: {cardId}");
    }

    /// <summary>Copies the look, not the art: the portrait stays with the card it was chosen for.</summary>
    private static void OnCopyPressed()
    {
        _clipboard = _state with { CardId = "", PortraitPath = null };
        _pasteButton?.Disabled = false;
    }

    private static void OnPastePressed()
    {
        if (_clipboard == null) return;
        _state = _clipboard with { CardId = _state.CardId, PortraitPath = _state.PortraitPath };
        ShowState();
    }

    private static void OnResetPressed()
    {
        _state = new CardHsvData { CardId = _state.CardId };
        ShowState();
    }

    private static void OnSaveDefaultButtonPressed()
    {
        if (_currentCard == null) return;

        string cardId = ArtContext.KeyFor(_currentCard);
        CardArtRoller.SaveDefaultHsvForCard(cardId, _state);
        MarkClean();
        ReloadCards();
        MainFile.Logger.Info($"Saved default for card: {cardId}");
    }

    private static void OnClearDefaultButtonPressed()
    {
        if (_currentCard == null) return;

        string cardId = ArtContext.KeyFor(_currentCard);
        if (CardArtRoller.GetDefaultHsvForCard(cardId) == null)
        {
            TaskHelper.RunSafely(ShowNotice("ARTROLLER-EDITOR_NO_DEFAULT"));
            return;
        }

        CardArtRoller.DeleteDefaultHsvForCard(cardId);
        ReloadCards();
        LoadStateFromRoll();
        MainFile.Logger.Info($"Cleared default for card: {cardId}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>A one-button popup, for a press that would otherwise change nothing on screen.</summary>
    private static async Task ShowNotice(string locKey)
    {
        var popup = NGenericPopup.Create();
        if (popup == null || NModalContainer.Instance == null) return;

        NModalContainer.Instance.Add(popup);
        await popup.WaitForConfirmation(
            body: new LocString("settings_ui", $"{locKey}.body"),
            header: new LocString("settings_ui", $"{locKey}.header"),
            noButton: null,
            yesButton: new LocString("settings_ui", $"{locKey}.ok"));
    }

    private static NCard? GetInspectedCard() =>
        NGame.Instance!.GetInspectCardScreen().GetNodeOrNull<NCard>("Card");

    private static TextureRect? GetInspectedPortrait()
    {
        var ncard = GetInspectedCard();
        if (ncard == null) return null;
        if (ncard.Model?.Rarity == CardRarity.Ancient) return ncard.GetNodeOrNull<TextureRect>("%AncientPortrait");
        return ncard.GetNodeOrNull<TextureRect>("%Portrait");
    }

    /// <summary>
    /// A card reached with the arrows has no grid tile of ours to redraw; it updates the next time
    /// the grid does.
    /// </summary>
    private static void ReloadCards()
    {
        var reload = AccessTools.Method(typeof(NCard), "Reload");
        if (reload == null) return;

        if (GetInspectedCard() is { } inspected) reload.Invoke(inspected, null);

        if (_libraryHolder is { CardNode: { } tile } && GodotObject.IsInstanceValid(tile)
            && _libraryHolder.CardModel == _currentCard)
            reload.Invoke(tile, null);
    }
}
