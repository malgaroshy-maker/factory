using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Floating 3D billboard text badge attached above physical components in the 3D viewport.
/// Displays live tag value and allows direct in-scene click forcing.
/// </summary>
public partial class FloatingTagBadge3D : Node3D
{
    private Label3D _label = null!;
    private TagTable? _tags;
    private string _tagId = string.Empty;

    public void Setup(string tagId, TagTable tags, Vector3 localOffset)
    {
        _tagId = tagId;
        _tags = tags;
        Position = localOffset;
    }

    public override void _Ready()
    {
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Text = "[TAG]",
            FontSize = 32,
            OutlineSize = 3,
            Modulate = new Color(0.9f, 0.9f, 0.95f),
        };
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_tags is null || string.IsNullOrEmpty(_tagId) || !_tags.Contains(_tagId))
            return;

        object visibleVal = _tags.Visible(_tagId);
        bool isForced = _tags.IsForced(_tagId);

        string forcedPrefix = isForced ? "⚡ " : "";
        string displayVal = visibleVal switch
        {
            bool b => b ? "TRUE" : "FALSE",
            int i => i.ToString(),
            float f => f.ToString("F1"),
            _ => visibleVal.ToString() ?? ""
        };

        _label.Text = $"{forcedPrefix}{_tagId}: {displayVal}";
        _label.Modulate = isForced
            ? new Color(1.0f, 0.7f, 0.2f) // Amber for forced
            : (visibleVal is bool bVal && bVal ? new Color(0.3f, 1.0f, 0.4f) : new Color(0.85f, 0.85f, 0.90f));
    }
}
