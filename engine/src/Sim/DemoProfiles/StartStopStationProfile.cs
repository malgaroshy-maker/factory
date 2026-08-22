using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Momentary buttons and a latching E-stop (§4.2). Plays the PLC's part of the
/// exercise: reacts to whatever the operator presses on the panel (Run mode
/// already lets you click it -- this is the one part built for that, per §2.7)
/// rather than pressing its own buttons, since the point of this scene is that
/// a human drives it.
///
/// <c>panel.estop</c> is normally closed: true means the circuit is healthy,
/// false means the mushroom is struck. A profile that got that backwards would
/// run happily with the E-stop wire cut, which is the exact bug NC wiring
/// exists to catch -- so this is worth getting right here, not just leaving
/// students to discover it.
/// </summary>
public sealed class StartStopStationProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    private bool _running;
    private bool _tripped;
    private bool _prevStart, _prevStop, _prevReset, _prevPresent;
    private int _produced;
    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;

    public void Start(TagTable tags)
    {
        _running = false;
        _tripped = false;
        _prevStart = _prevStop = _prevReset = _prevPresent = false;
        _produced = 0;
        _elapsed = 0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        Apply(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        bool start = Bit(tags, "panel.start");
        bool stop = Bit(tags, "panel.stop");
        bool reset = Bit(tags, "panel.reset");
        bool healthy = Bit(tags, "panel.estop");
        bool present = Bit(tags, "part_present.detect");

        bool startEdge = start && !_prevStart;
        bool stopEdge = stop && !_prevStop;
        bool resetEdge = reset && !_prevReset;
        bool presentEdge = present && !_prevPresent;
        _prevStart = start; _prevStop = stop; _prevReset = reset; _prevPresent = present;

        if (!healthy) _tripped = true;
        else if (resetEdge) _tripped = false;

        if (_tripped) _running = false;
        else if (stopEdge) _running = false;
        else if (startEdge) _running = true;

        if (presentEdge) _produced++;

        _elapsed += delta;
        if (_running)
        {
            if (_elapsed >= _nextToggle)
            {
                _emitFlag = !_emitFlag;
                _nextToggle = _elapsed + EmitHalfPeriod;
            }
        }
        else
        {
            _emitFlag = false;
            _nextToggle = _elapsed + EmitHalfPeriod;
        }

        Apply(tags);
    }

    private void Apply(TagTable tags)
    {
        Set(tags, "belt.rotate", _running);
        Set(tags, "emitter.emit", _emitFlag);
        Set(tags, "produced.value", _produced);
        // Mutually exclusive: green while running, red while tripped, yellow
        // for stopped-but-healthy -- lamp state and belt state can never
        // disagree if there is exactly one true at a time.
        Set(tags, "tower.green", _running);
        Set(tags, "tower.red", _tripped);
        Set(tags, "tower.yellow", !_running && !_tripped);
    }

    private static bool Bit(TagTable tags, string tagId) =>
        tags.Contains(tagId) && tags.Visible(tagId) is true;

    private static void Set(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }
}
