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
        CustomMinimumSize = new Vector2(300, 400);
        SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.KeepSize, 20);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(300, 400),
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
            CustomMinimumSize = new Vector2(280, 340),
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

            var nameLabel = new Label
            {
                Text = tag.Id,
                CustomMinimumSize = new Vector2(120, 0),
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            row.AddChild(nameLabel);

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
