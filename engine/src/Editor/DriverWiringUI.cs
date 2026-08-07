using System;
using System.Collections.Generic;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Interactive I/O Driver Wiring Panel (F4 key).
/// Centered modal UI allowing users to visually map PLC addresses (%I0.0, %Q0.0, OPC UA NodeIDs)
/// directly to FactoryForge component tags.
/// </summary>
public partial class DriverWiringUI : Control
{
    private TagTable? _tags;
    private readonly Dictionary<string, string> _mappings = new();
    private VBoxContainer _plcTagsList = null!;
    private VBoxContainer _simTagsList = null!;
    private Label _statusLabel = null!;
    private string? _selectedPlcAddress;

    public override void _Ready()
    {
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 1. Dark semi-transparent background overlay
        var overlay = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.75f),
        };
        overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        // 2. CenterContainer guarantees 100% viewport centering
        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centerContainer);

        // 3. Main modal card
        var modal = new PanelContainer
        {
            CustomMinimumSize = new Vector2(980, 620),
        };
        centerContainer.AddChild(modal);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        modal.AddChild(margin);

        var mainBox = new VBoxContainer();
        margin.AddChild(mainBox);

        // Header
        var header = new HBoxContainer();
        mainBox.AddChild(header);

        var title = new Label
        {
            Text = "🔌 VISUAL I/O DRIVER WIRING (F4)",
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(title);

        var closeBtn = new Button { Text = " ❌ Close " };
        closeBtn.Pressed += () => Visible = false;
        header.AddChild(closeBtn);

        var subtitle = new Label
        {
            Text = "Click a PLC Address on the left, then click a FactoryForge Tag on the right to bind them.",
        };
        subtitle.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.80f));
        mainBox.AddChild(subtitle);

        mainBox.AddChild(new HSeparator());

        // Split columns
        var splitBox = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        mainBox.AddChild(splitBox);

        // Left Column: PLC Addresses
        var leftBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        splitBox.AddChild(leftBox);
        var leftTitle = new Label
        {
            Text = "⚡ PLC / DRIVER SIGNALS (%I / %Q / OPC UA)",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        leftTitle.AddThemeFontSizeOverride("font_size", 14);
        leftBox.AddChild(leftTitle);

        var leftScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        leftBox.AddChild(leftScroll);
        _plcTagsList = new VBoxContainer();
        leftScroll.AddChild(_plcTagsList);

        // Middle Divider
        splitBox.AddChild(new VSeparator());

        // Right Column: Sim Tags
        var rightBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        splitBox.AddChild(rightBox);
        var rightTitle = new Label
        {
            Text = "🏭 FACTORYFORGE COMPONENT TAGS",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        rightTitle.AddThemeFontSizeOverride("font_size", 14);
        rightBox.AddChild(rightTitle);

        var rightScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        rightBox.AddChild(rightScroll);
        _simTagsList = new VBoxContainer();
        rightScroll.AddChild(_simTagsList);

        mainBox.AddChild(new HSeparator());

        // Footer status bar
        var footer = new HBoxContainer();
        mainBox.AddChild(footer);

        _statusLabel = new Label
        {
            Text = "Active Mappings: 0",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _statusLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.4f));
        footer.AddChild(_statusLabel);

        var autoMapBtn = new Button { Text = " ⚡ Auto-Map Siemens S7 Standard " };
        autoMapBtn.Pressed += AutoMapByName;
        footer.AddChild(autoMapBtn);

        var clearBtn = new Button { Text = " 🗑️ Clear Mappings " };
        clearBtn.Pressed += ClearAllMappings;
        footer.AddChild(clearBtn);
    }

    public void Setup(TagTable tags)
    {
        _tags = tags;
        RebuildWiringList();
    }

    public void ToggleVisibility()
    {
        Visible = !Visible;
        if (Visible) RebuildWiringList();
    }

    public void RebuildWiringList()
    {
        if (_tags is null) return;

        foreach (Node child in _plcTagsList.GetChildren()) child.QueueFree();
        foreach (Node child in _simTagsList.GetChildren()) child.QueueFree();

        // Sample PLC Address List
        string[] samplePlcAddrs = new string[]
        {
            "%Q0.0 (Conveyor 1 Motor)",
            "%Q0.1 (Pusher 1 Solenoid)",
            "%Q0.2 (Stack Light Green)",
            "%Q0.3 (Stack Light Yellow)",
            "%Q0.4 (Stack Light Red)",
            "%I0.0 (Sensor Low Optical)",
            "%I0.1 (Sensor High Optical)",
            "%I0.2 (Pusher 1 Retracted)",
            "%I0.3 (Pusher 1 Extended)",
        };

        foreach (string addr in samplePlcAddrs)
        {
            string addressKey = addr.Split(' ')[0];
            bool isSelected = _selectedPlcAddress == addressKey;

            var btn = new Button
            {
                Text = isSelected ? $"👉 {addr}" : addr,
                CustomMinimumSize = new Vector2(400, 36),
                Alignment = HorizontalAlignment.Left,
            };

            btn.Pressed += () => SelectPlcAddress(addressKey);
            _plcTagsList.AddChild(btn);
        }

        // Sim Tags List
        foreach (var tag in _tags)
        {
            var row = new HBoxContainer();
            var label = new Label
            {
                Text = $"{tag.Id} [{tag.Kind}]",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            row.AddChild(label);

            bool isMapped = _mappings.ContainsKey(tag.Id);
            string mappedTo = isMapped ? $"[Mapped: {_mappings[tag.Id]}]" : "🔗 Click to Map";

            var mapBtn = new Button
            {
                Text = mappedTo,
                CustomMinimumSize = new Vector2(170, 32),
            };

            if (isMapped)
            {
                mapBtn.AddThemeColorOverride("font_color", new Color(0.3f, 1.0f, 0.4f));
            }

            string tagId = tag.Id;
            mapBtn.Pressed += () => MapSelectedToTag(tagId);
            row.AddChild(mapBtn);

            _simTagsList.AddChild(row);
        }

        UpdateStatus();
    }

    private void SelectPlcAddress(string addr)
    {
        _selectedPlcAddress = addr;
        RebuildWiringList();
        _statusLabel.Text = $"Selected PLC Address: {addr}. Click a Sim Tag map button on the right.";
    }

    private void MapSelectedToTag(string tagId)
    {
        if (string.IsNullOrEmpty(_selectedPlcAddress)) return;

        _mappings[tagId] = _selectedPlcAddress;
        RebuildWiringList();
        _statusLabel.Text = $"Mapped '{_selectedPlcAddress}' ➔ '{tagId}'";
    }

    private void AutoMapByName()
    {
        if (_tags is null) return;
        _mappings["conveyor.rotate"] = "%Q0.0";
        _mappings["pusher.extend"] = "%Q0.1";
        _mappings["stack_light.green"] = "%Q0.2";
        _mappings["stacklight_1.green"] = "%Q0.2";
        _mappings["stacklight_1.yellow"] = "%Q0.3";
        _mappings["stacklight_1.red"] = "%Q0.4";
        _mappings["sensor_low.detect"] = "%I0.0";
        _mappings["sensor_high.detect"] = "%I0.1";
        _mappings["pusher.retracted"] = "%I0.2";
        _mappings["pusher.extended"] = "%I0.3";

        RebuildWiringList();
        _statusLabel.Text = "Auto-mapped active tags to Siemens S7 I/O addresses";
    }

    private void ClearAllMappings()
    {
        _mappings.Clear();
        _selectedPlcAddress = null;
        RebuildWiringList();
        _statusLabel.Text = "Cleared all I/O mappings";
    }

    private void UpdateStatus()
    {
        _statusLabel.Text = $"Active I/O Mappings: {_mappings.Count}";
    }
}
