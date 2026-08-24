using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Checkweighing and material (§4.5, OP-07). Runs both decks and the emitter
/// (the template's own <c>metal_every</c> setting handles the material cadence
/// — nothing here needs to know a carton is metal to make one appear); the
/// scale and the inductive sensor publish their own readings every tick
/// (<c>SceneEditor</c>'s dispatch, not this profile's job).
///
/// What this profile adds is the decision. The panel's pot is a <b>reject
/// limit in grams</b>, so the scale stops being a readout and becomes a
/// verdict: each carton is judged on its peak weight as it leaves the deck,
/// and an over-limit one lights the panel's red lamp. On this line the
/// steel cartons are also the heavy ones, so the checkweigher and the
/// inductive sensor should flag the same cartons — two independent
/// instruments agreeing, which is a far stronger statement than either one
/// reading non-zero.
/// </summary>
public sealed class RollerLineWeighingProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    /// <summary>Below this the load cell reads empty. Cartons on this line
    /// weigh 720 g and up, so anything at all on the deck clears it.</summary>
    private const double ScaleZero = 0.5;

    /// <summary>Grams, used only on a line built without a panel. The shipped
    /// template's pot ships at 3000 g, which falls between the cardboard
    /// cartons (720 g and 2160 g) and the steel ones (4320 g and 12960 g).</summary>
    private const double DefaultRejectLimit = 3000.0;

    private readonly OperatorStation _station = new();

    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;
    private bool _onScale;
    private double _peak;
    private int _rejects;

    public void Start(TagTable tags)
    {
        _elapsed = 0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        _onScale = false;
        _peak = 0.0;
        _rejects = 0;
        _station.Begin();
        Apply(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
        _elapsed += delta;

        if (_station.Running)
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

        double weight = OperatorStation.Num(tags, "scale.weight");
        if (weight > ScaleZero)
        {
            if (!_onScale) _peak = 0.0;
            _onScale = true;
            if (weight > _peak) _peak = weight;
        }
        else if (_onScale)
        {
            // Judged on the peak as it leaves, not on whatever the cell
            // happened to read as the carton rolled off the end of the deck.
            _onScale = false;
            if (_peak > _station.Setpoint(tags, DefaultRejectLimit)) _rejects++;
        }

        OperatorStation.Set(tags, "weight_readout.value", (int)System.Math.Round(weight));
        Apply(tags);
    }

    private void Apply(TagTable tags)
    {
        OperatorStation.Set(tags, "infeed.rotate", _station.Running);
        OperatorStation.Set(tags, "scale.rotate", _station.Running);
        OperatorStation.Set(tags, "emitter.emit", _emitFlag);
        _station.Lamps(tags);
        // Red means "something needs attention" — a trip, or a carton that
        // came over the limit. A reject nobody can see is a reject nobody
        // acts on, and this line has no reject gate to watch instead.
        OperatorStation.Set(tags, "panel.red", _station.Tripped || _rejects > 0);
    }
}
