using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// What the open scene is asking you to build (BR-03).
///
/// Every shipped template teaches something specific, and until this existed
/// the app never said what. You opened the heat-treat station, saw a hot plate,
/// and had to guess; the lesson lived in `tools/try_scene.py`, which is the
/// last place a person learning PLC programming will look.
///
/// Three sections, because a brief that is only prose gets skimmed: **the
/// task** in a sentence or two, **the tags** your program drives and reads,
/// and **how you know it works** — which is the part that turns "make the belt
/// go" into something a person can check themselves.
///
/// Shown on demand rather than on open. A panel that covers the scene the
/// moment it loads is a panel people learn to dismiss without reading.
/// </summary>
public partial class TaskBriefUI : Control
{
    private PanelContainer _card = null!;
    private Label _title = null!;
    private Label _task = null!;
    private Label _uses = null!;
    private Label _done = null!;
    private Label _usesHeading = null!;
    private Label _doneHeading = null!;
    private Label _empty = null!;

    public override void _Ready()
    {
        // AnchorsAndOffsets, not Anchors alone: setting the anchors without the
        // offsets leaves the control at its old zero size, so the centring
        // container below centres inside nothing and the card lands in the
        // top-left corner on top of the palette. Same call KeyHelpUI makes.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        centre.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(centre);

        _card = new PanelContainer { CustomMinimumSize = new Vector2(620, 0) };
        centre.AddChild(_card);

        var margin = new MarginContainer();
        foreach (string side in new[] { "margin_top", "margin_bottom", "margin_left", "margin_right" })
        {
            margin.AddThemeConstantOverride(side, 20);
        }
        _card.AddChild(margin);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        margin.AddChild(box);

        _title = new Label { Text = "YOUR TASK" };
        _title.AddThemeFontSizeOverride("font_size", 17);
        _title.AddThemeColorOverride("font_color", Heading);
        box.AddChild(_title);

        _task = Body(box);
        _usesHeading = Section("TAGS YOUR PROGRAM USES");
        box.AddChild(_usesHeading);
        _uses = Body(box);
        _doneHeading = Section("HOW YOU KNOW IT WORKS");
        box.AddChild(_doneHeading);
        _done = Body(box);

        _empty = new Label
        {
            Text = "This scene is one you built, so there is no set task.\n"
                   + "Open a template from the start screen for one.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Visible = false,
        };
        box.AddChild(_empty);

        var hint = new Label { Text = "T or Esc to close" };
        hint.AddThemeFontSizeOverride("font_size", 11);
        hint.AddThemeColorOverride("font_color", new Color(0.62f, 0.65f, 0.70f));
        box.AddChild(hint);
    }

    private static readonly Color Heading = new(0.98f, 0.80f, 0.35f);

    private static Label Section(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", Heading);
        return label;
    }

    private static Label Body(VBoxContainer box)
    {
        var label = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(580, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(label);
        return label;
    }

    /// <summary>Point the panel at a scene. A scene with no brief — anything
    /// somebody built themselves — says so rather than opening blank.</summary>
    public void ShowFor(string sceneName)
    {
        // Named rather than `var`, and not for style: `--self-test=parity`'s
        // A6 check reads a type as unused when nothing outside its own file
        // mentions it, and every reader of this one had inferred it away.
        TemplateBrief? brief = TemplateManifest.BriefForScene(sceneName);
        bool has = brief is not null;

        _task.Visible = has;
        _uses.Visible = has;
        _done.Visible = has;
        _usesHeading.Visible = has;
        _doneHeading.Visible = has;
        _empty.Visible = !has;

        if (brief is not null)
        {
            _task.Text = brief.Task;
            _uses.Text = brief.Uses;
            _done.Text = brief.Done;
        }

        Visible = true;
    }

    public void Toggle(string sceneName)
    {
        if (Visible) { Visible = false; return; }
        ShowFor(sceneName);
    }

    /// <summary>Close it if it is open, and say whether it was. Escape is
    /// spent on several things — a placement, the fault tool, the key overlay —
    /// so the caller needs to know whether this one took it.</summary>
    public bool DismissIfOpen()
    {
        if (!Visible) return false;
        Visible = false;
        return true;
    }
}
