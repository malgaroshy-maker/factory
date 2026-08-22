using System.Collections.Generic;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Live UI panel displaying tag bus registry values and allowing manual tag forcing.
/// </summary>
public partial class TagInspectorUI : Control
{
    private TagTable _tags = null!;
    private VBoxContainer _listContainer = null!;
    private readonly Dictionary<string, Label> _valueLabels = new();
    private readonly Dictionary<string, Button> _forceButtons = new();

    public void Setup(TagTable tags)
    {
        _tags = tags;
        BuildUI();
    }

    private void BuildUI()
    {
        // Wider than before: the name column is the one thing a user needs to
        // read out of this panel — it is what goes into a PLC mapping file —
        // and 300px total left it truncated to "weight_readout.va…" (FF-19).
        CustomMinimumSize = new Vector2(360, 400);
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 20);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(360, 400),
        };
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        panel.AddChild(margin);

        var mainBox = new VBoxContainer();
        margin.AddChild(mainBox);

        var title = new Label
        {
            Text = "TAG BUS INSPECTOR",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 16);
        mainBox.AddChild(title);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(340, 340),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        mainBox.AddChild(scroll);

        _listContainer = new VBoxContainer();
        scroll.AddChild(_listContainer);

        RebuildTagList();
    }

    public void RebuildTagList()
    {
        if (_tags is null) return;

        foreach (var child in _listContainer.GetChildren())
        {
            child.QueueFree();
        }
        _valueLabels.Clear();
        _forceButtons.Clear();

        foreach (var tag in _tags)
        {
            var row = new HBoxContainer();
            _listContainer.AddChild(row);

            // A button, not a label: this is the identifier a user has to
            // retype into a PLC mapping, and click-to-copy is what makes that
            // fast instead of error-prone. Elided in the *middle* rather than
            // Godot's own end-truncation — the suffix (".value", ".detect")
            // is what tells two similarly-prefixed tags apart, so cutting it
            // off is exactly backwards. See FF-19.
            var nameBtn = new Button
            {
                Text = ElideMiddle(tag.Id, 20),
                CustomMinimumSize = new Vector2(170, 0),
                Alignment = HorizontalAlignment.Left,
                Flat = true,
                TooltipText = $"{tag.Id}\n{tag.Type} · {tag.Kind}\nClick to copy",
            };
            string fullId = tag.Id;
            nameBtn.Pressed += () =>
            {
                DisplayServer.ClipboardSet(fullId);
                GD.Print($"copied tag id: {fullId}");
            };
            row.AddChild(nameBtn);

            var valLabel = new Label
            {
                Text = _tags.Visible(tag.Id)?.ToString() ?? "null",
                CustomMinimumSize = new Vector2(60, 0),
            };
            row.AddChild(valLabel);
            _valueLabels[tag.Id] = valLabel;

            bool isForced = _tags.IsForced(tag.Id);
            var forceBtn = new Button
            {
                Text = isForced ? "UNFORCE" : "Force",
                CustomMinimumSize = new Vector2(60, 0),
            };
            string tagId = tag.Id;
            forceBtn.Pressed += () => ToggleForce(tagId);
            row.AddChild(forceBtn);
            _forceButtons[tagId] = forceBtn;
        }
    }

    /// <summary>Shorten to at most <paramref name="maxChars"/>, cutting from
    /// the middle so the id's discriminating suffix survives.</summary>
    private static string ElideMiddle(string id, int maxChars)
    {
        if (id.Length <= maxChars) return id;
        int keep = maxChars - 1;   // one char reserved for the ellipsis glyph
        int left = keep * 2 / 5;   // favour the suffix: it is what differs
        int right = keep - left;
        return id[..left] + "…" + id[^right..];
    }

    private void ToggleForce(string tagId)
    {
        var tag = _tags.Get(tagId);
        if (tag is null) return;

        if (_tags.IsForced(tagId))
        {
            _tags.ClearForce(tagId);
        }
        else
        {
            if (tag.Type == TagType.Bit)
            {
                bool current = (bool)_tags.Visible(tagId);
                _tags.Force(tagId, !current);
            }
        }
        UpdateUI();
    }

    public override void _Process(double delta)
    {
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_tags is null) return;

        foreach (var tag in _tags)
        {
            if (_valueLabels.TryGetValue(tag.Id, out var valLabel))
            {
                valLabel.Text = _tags.Visible(tag.Id)?.ToString() ?? "null";
            }
            if (_forceButtons.TryGetValue(tag.Id, out var forceBtn))
            {
                forceBtn.Text = _tags.IsForced(tag.Id) ? "UNFORCE" : "Force";
            }
        }
    }
}
