using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Checkweighing and material (§4.5). Runs both decks and the emitter (the
/// template's own <c>metal_every</c> setting handles the material cadence --
/// nothing here needs to know a carton is metal to make one appear); the scale
/// and the inductive sensor already publish their own readings every tick
/// (<c>SceneEditor</c>'s dispatch, not this profile's job), so all that is left
/// to drive is mirroring the scale onto the display.
/// </summary>
public sealed class RollerLineWeighingProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;

    public void Start(TagTable tags)
    {
        _elapsed = 0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        Set(tags, "infeed.rotate", true);
        Set(tags, "scale.rotate", true);
    }

    public void Tick(double delta, TagTable tags)
    {
        _elapsed += delta;
        if (_elapsed >= _nextToggle)
        {
            _emitFlag = !_emitFlag;
            _nextToggle = _elapsed + EmitHalfPeriod;
            Set(tags, "emitter.emit", _emitFlag);
        }

        if (tags.Contains("scale.weight"))
        {
            int weight = System.Convert.ToInt32(tags.Visible("scale.weight"));
            Set(tags, "weight_readout.value", weight);
        }
    }

    private static void Set(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }
}
