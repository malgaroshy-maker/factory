using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// A small, honest control program for one shipped scene -- the same program a
/// student would be asked to write. <see cref="DemoDriver"/> looks one of these
/// up by scene name and runs it instead of hardcoding one scene's logic itself
/// (UX-16). Every write goes through <c>TagTable</c> exactly the way a real PLC
/// driver would, so nothing here has a back door into the simulation.
/// </summary>
public interface IDemoProfile
{
    /// <summary>Reset all state and drive any outputs that should be true the
    /// instant the demo starts (e.g. a running conveyor).</summary>
    void Start(TagTable tags);

    /// <summary>Called every frame while the demo is active.</summary>
    void Tick(double delta, TagTable tags);
}
