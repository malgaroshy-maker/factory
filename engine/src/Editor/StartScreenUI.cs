using System.Collections.Generic;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// What you see when FactoryForge opens.
///
/// Before this, the engine came up straight into the sorting demo with no way
/// to start from anything else and no statement of what the keys do — fine when
/// the only person running it wrote it, hopeless as a first impression.
///
/// It is an overlay on top of the normal startup rather than a separate scene.
/// The engine still builds the sorting line behind it, so choosing "Sorting by
/// height" is just dismissing the panel, and every other choice goes through
/// the same clear/load paths the toolbar already uses and the self-tests
/// already cover.
/// </summary>
public partial class StartScreenUI : Control
{
    /// <summary>Start from a shipped template file (res:// path).</summary>
    [Signal] public delegate void TemplateChosenEventHandler(string path);
    /// <summary>Start from the built-in sorting line.</summary>
    [Signal] public delegate void DefaultSceneChosenEventHandler();
    /// <summary>Start from nothing.</summary>
    [Signal] public delegate void EmptySceneChosenEventHandler();
    /// <summary>Open a scene the user saved.</summary>
    [Signal] public delegate void OpenRequestedEventHandler(string path);

    private sealed record Template(string Title, string Blurb, string Path);

    /// <summary>
    /// Each one teaches a different thing, which is the point of having more
    /// than one. The blurb says what you would learn, not what parts are in it.
    /// </summary>
    private static readonly Template[] Templates =
    {
        new("Sorting by height",
            "The reference line: sensors, a diverter and two counters. Sorts tall "
            + "cartons down a chute and lets short ones pass.",
            ""),          // empty path = the built-in scene
        new("Start / stop station",
            "A conveyor and an operator panel. Momentary buttons, a latching "
            + "E-stop, and a lamp that has to reflect the real state.",
            "res://templates/start_stop_station.json"),
        new("Tank level control",
            "Analog end to end. A modulating valve and a level transmitter, with "
            + "outflow that varies with level — a PID tuned full will overshoot empty.",
            "res://templates/tank_level_control.json"),
        new("Light curtain sorting",
            "Sorting on a measurement instead of two bits. The curtain reports how "
            + "tall each carton is, so you choose the threshold in code.",
            "res://templates/light_curtain_sorting.json"),
        new("Roller line with weighing",
            "A roller deck, a checkweigher and an inductive sensor that sees metal "
            + "only — cardboard passes it as if the lane were empty.",
            "res://templates/roller_line_weighing.json"),
    };

    private const string RecentPath = "user://recent_scenes.json";
    private VBoxContainer _recentBox = null!;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var backdrop = new ColorRect { Color = new Color(0.05f, 0.06f, 0.08f, 0.93f) };
        backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);

        var card = new PanelContainer { CustomMinimumSize = new Vector2(980, 640) };
        centre.AddChild(card);

        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_top", "margin_bottom", "margin_left", "margin_right" })
            margin.AddThemeConstantOverride(side, 28);
        card.AddChild(margin);

        var page = new VBoxContainer();
        margin.AddChild(page);

        var title = new Label { Text = "FactoryForge" };
        title.AddThemeFontSizeOverride("font_size", 34);
        page.AddChild(title);

        var subtitle = new Label
        {
            Text = "A 3D factory to write PLC programs against. Pick something to start from.",
        };
        subtitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.75f, 0.80f));
        page.AddChild(subtitle);

        page.AddChild(new HSeparator());

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 22);
        page.AddChild(columns);

        BuildTemplateColumn(columns);
        columns.AddChild(new VSeparator());
        BuildSideColumn(columns);

        page.AddChild(new HSeparator());
        BuildControlsFooter(page);
    }

    private void BuildTemplateColumn(HBoxContainer columns)
    {
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(left);

        var heading = new Label { Text = "START FROM A TEMPLATE" };
        heading.AddThemeFontSizeOverride("font_size", 13);
        heading.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.35f));
        left.AddChild(heading);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // Wrap the blurbs instead of letting them run off the right edge
            // behind a horizontal scrollbar nobody will think to drag.
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        left.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(list);

        foreach (var template in Templates)
        {
            var button = new Button
            {
                // Height only: a minimum *width* here would force the button
                // wider than the column it lives in, which is what clipped the
                // text in the first place.
                CustomMinimumSize = new Vector2(0, 70),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                Text = $"  {template.Title}\n  {template.Blurb}",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                TooltipText = template.Blurb,
            };
            button.AddThemeFontSizeOverride("font_size", 13);

            string path = template.Path;
            button.Pressed += () =>
            {
                if (path.Length == 0) EmitSignal(SignalName.DefaultSceneChosen);
                else EmitSignal(SignalName.TemplateChosen, path);
                Dismiss();
            };
            list.AddChild(button);
        }
    }

    private void BuildSideColumn(HBoxContainer columns)
    {
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddChild(right);

        var heading = new Label { Text = "OR" };
        heading.AddThemeFontSizeOverride("font_size", 13);
        heading.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.35f));
        right.AddChild(heading);

        var blank = new Button
        {
            Text = "  Empty scene",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(300, 38),
            TooltipText = "No parts and no tags — build a line from the palette",
        };
        blank.Pressed += () => { EmitSignal(SignalName.EmptySceneChosen); Dismiss(); };
        right.AddChild(blank);

        var open = new Button
        {
            Text = "  Open a saved scene…",
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(300, 38),
        };
        open.Pressed += ShowOpenDialog;
        right.AddChild(open);

        right.AddChild(new HSeparator());

        var recentHeading = new Label { Text = "RECENT" };
        recentHeading.AddThemeFontSizeOverride("font_size", 13);
        recentHeading.AddThemeColorOverride("font_color", new Color(0.98f, 0.80f, 0.35f));
        right.AddChild(recentHeading);

        _recentBox = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        right.AddChild(_recentBox);
        RebuildRecent();

        var quit = new Button { Text = "  Quit", Alignment = HorizontalAlignment.Left };
        quit.Pressed += () => GetTree().Quit();
        right.AddChild(quit);
    }

    private void BuildControlsFooter(VBoxContainer page)
    {
        // The keys used to live only in a markdown file, which is no use to
        // somebody who has just opened the program.
        var grid = new GridContainer { Columns = 4 };
        // Without this the next column's key sits flush against the previous
        // action, and "Edit / Run mode" reads as though Space were part of it.
        grid.AddThemeConstantOverride("h_separation", 26);
        grid.AddThemeConstantOverride("v_separation", 6);
        page.AddChild(grid);

        var keys = new (string Key, string Action)[]
        {
            ("F1", "Edit / Run mode"),
            ("Space", "Pause / resume"),
            ("Ctrl+R", "Reset the run"),
            ("C", "Orbit / fly camera"),
            ("M", "Move selected part"),
            ("R", "Rotate before placing"),
            ("Del", "Delete selected part"),
            ("Ctrl+Z / Y", "Undo / redo"),
            ("F4", "I/O wiring and export"),
            ("F5", "Connect a PLC driver"),
            ("Esc", "Cancel placement"),
            ("", ""),
        };

        foreach (var (key, action) in keys)
        {
            if (key.Length == 0) { grid.AddChild(new Control()); continue; }

            var row = new HBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
            var keyLabel = new Label { Text = key, CustomMinimumSize = new Vector2(74, 0) };
            keyLabel.AddThemeColorOverride("font_color", new Color(0.55f, 0.85f, 1.0f));
            keyLabel.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(keyLabel);

            var actionLabel = new Label { Text = action };
            actionLabel.AddThemeFontSizeOverride("font_size", 12);
            actionLabel.AddThemeColorOverride("font_color", new Color(0.72f, 0.75f, 0.80f));
            row.AddChild(actionLabel);

            grid.AddChild(row);
        }
    }

    private void ShowOpenDialog()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.json ; FactoryForge scene" },
            Title = "Open scene",
            Size = new Vector2I(760, 520),
        };
        dialog.FileSelected += (path) =>
        {
            EmitSignal(SignalName.OpenRequested, path);
            dialog.QueueFree();
            Dismiss();
        };
        dialog.Canceled += dialog.QueueFree;
        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void Dismiss() => Visible = false;

    /// <summary>Show it again — the toolbar's way back to this screen.</summary>
    public void Reopen()
    {
        RebuildRecent();
        Visible = true;
    }

    // --- recent files ---

    private void RebuildRecent()
    {
        foreach (var child in _recentBox.GetChildren()) child.QueueFree();

        var recent = LoadRecent();
        if (recent.Count == 0)
        {
            var none = new Label { Text = "  nothing yet" };
            none.AddThemeFontSizeOverride("font_size", 12);
            none.AddThemeColorOverride("font_color", new Color(0.5f, 0.52f, 0.56f));
            _recentBox.AddChild(none);
            return;
        }

        foreach (string path in recent)
        {
            var button = new Button
            {
                Text = "  " + path.GetFile(),
                TooltipText = path,
                Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(300, 32),
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            string captured = path;
            button.Pressed += () =>
            {
                EmitSignal(SignalName.OpenRequested, captured);
                Dismiss();
            };
            _recentBox.AddChild(button);
        }
    }

    public static List<string> LoadRecent()
    {
        var result = new List<string>();
        if (!Godot.FileAccess.FileExists(RecentPath)) return result;

        using var file = Godot.FileAccess.Open(RecentPath, Godot.FileAccess.ModeFlags.Read);
        if (Json.ParseString(file?.GetAsText() ?? "").Obj is not Godot.Collections.Array list)
            return result;

        foreach (var entry in list)
        {
            string path = entry.AsString();
            // A file that has been moved or deleted should not sit in the list
            // offering to open something that is not there.
            if (path.Length > 0 && Godot.FileAccess.FileExists(path)) result.Add(path);
        }
        return result;
    }

    /// <summary>Record a scene as recently used, most recent first.</summary>
    public static void Remember(string path)
    {
        if (path.StartsWith("res://")) return;   // templates are not "recent files"

        var recent = LoadRecent();
        recent.Remove(path);
        recent.Insert(0, path);
        while (recent.Count > 6) recent.RemoveAt(recent.Count - 1);

        var array = new Godot.Collections.Array();
        foreach (string entry in recent) array.Add(entry);

        using var file = Godot.FileAccess.Open(RecentPath, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(Json.Stringify(array));
    }
}
