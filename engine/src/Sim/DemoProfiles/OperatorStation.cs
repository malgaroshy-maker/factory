using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// One control panel, scanned the way a PLC scans it — shared by every demo
/// profile so all five lines answer to Start, Stop, Reset and the mushroom in
/// exactly the same way (OP-02).
///
/// Before this, one profile out of five read the panel. The other four ran
/// regardless: you could strike the E-stop on a running demo and watch the
/// belt keep going, which teaches the opposite of what an E-stop is for. The
/// panel was wired to the tag bus and the tag bus was wired to nobody.
///
/// A demo <b>starts running</b> rather than waiting for a press — 🎬 Demo is
/// "watch it run", and a button that produces a still factory reads as broken.
/// From the first tick onward the panel is authoritative: Stop stops it, the
/// mushroom trips it, and only Reset then Start bring it back.
///
/// Mirrors <c>Station</c> in <c>tools/try_scene.py</c>, which drives the same
/// five scenes over the wire.
/// </summary>
public sealed class OperatorStation
{
    private readonly string _prefix;
    private bool _prevStart, _prevStop, _prevReset;

    public OperatorStation(string prefix = "panel") => _prefix = prefix;

    /// <summary>True while the line should be moving.</summary>
    public bool Running { get; private set; }

    /// <summary>Latched by the mushroom, cleared only by Reset. A trip that
    /// cleared itself when the mushroom popped back out would restart the line
    /// under whoever was still working on it.</summary>
    public bool Tripped { get; private set; }

    /// <summary>Rising edge of Start on the scan just gone — for a controller
    /// that needs to do something once per press rather than while running,
    /// like beginning a fresh batch.</summary>
    public bool StartEdge { get; private set; }

    /// <summary>Put the station back the way a scene reset leaves it, and set
    /// the line running. Called from a profile's <c>Start</c>.</summary>
    public void Begin()
    {
        Running = true;
        Tripped = false;
        StartEdge = false;
        _prevStart = _prevStop = _prevReset = false;
    }

    public void Scan(TagTable tags)
    {
        bool start = Bit(tags, $"{_prefix}.start");
        bool stop = Bit(tags, $"{_prefix}.stop");
        bool reset = Bit(tags, $"{_prefix}.reset");
        bool healthy = !tags.Contains($"{_prefix}.estop") || Bit(tags, $"{_prefix}.estop");

        StartEdge = start && !_prevStart;
        bool stopEdge = stop && !_prevStop;
        bool resetEdge = reset && !_prevReset;
        _prevStart = start; _prevStop = stop; _prevReset = reset;

        if (!healthy) Tripped = true;
        else if (resetEdge) Tripped = false;

        if (Tripped || stopEdge) Running = false;
        else if (StartEdge && healthy) Running = true;
    }

    /// <summary>Stop the line from the controller's own side — a batch
    /// reaching its target, say. Distinct from the operator pressing Stop only
    /// in who decided; the line is equally off either way.</summary>
    public void HoldOff() => Running = false;

    /// <summary>
    /// Where the panel's pot is, in the scene's own units. The template owns
    /// the range, so this is already metres, grams or seconds — there is no
    /// percent to rescale and therefore no second copy of the range to drift
    /// out of step with the plate on the panel.
    /// </summary>
    /// <param name="fallback">Used when the scene has no panel at all, so a
    /// profile still runs against a line somebody built without one.</param>
    public double Setpoint(TagTable tags, double fallback) =>
        tags.Contains($"{_prefix}.setpoint")
            ? System.Convert.ToDouble(tags.Visible($"{_prefix}.setpoint"))
            : fallback;

    /// <summary>Panel lamps and, where the scene has one, the stack light.
    /// Exactly one tower stage is lit at a time: yellow is stopped-but-healthy,
    /// because a tower with nothing lit says "no power", not "idle".</summary>
    public void Lamps(TagTable tags)
    {
        Set(tags, $"{_prefix}.green", Running);
        Set(tags, $"{_prefix}.red", Tripped);
        Set(tags, "tower.green", Running);
        Set(tags, "tower.red", Tripped);
        Set(tags, "tower.yellow", !Running && !Tripped);
        Set(tags, "stack_light.green", Running);
    }

    public static bool Bit(TagTable tags, string tagId) =>
        tags.Contains(tagId) && tags.Visible(tagId) is true;

    public static void Set(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }

    public static double Num(TagTable tags, string tagId) =>
        tags.Contains(tagId) ? System.Convert.ToDouble(tags.Visible(tagId)) : 0.0;
}
