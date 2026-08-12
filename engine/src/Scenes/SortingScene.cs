using System.Collections.Generic;
using FactoryForge.TagBus;

namespace FactoryForge.Scenes;

/// <summary>
/// Headless "sorting by height" scene — a faithful C# port of
/// harness/scene.py. No physics or 3D yet: boxes are points on a line.
///
/// Keeping this identical to the Python harness is what lets the same
/// regression assertions hold for both, so the engine can be swapped in
/// underneath the drivers without changing a single test's expectations.
/// </summary>
public sealed class SortingScene
{
    public const double EmitterPos = 0.0;
    public const double SensorLowPos = 1.5;
    public const double SensorHighPos = 2.0;
    public const double PusherPos = 2.5;
    public const double RemoverPos = 3.0;

    public const double BoxLength = 0.2;
    public const double SensorWindow = BoxLength;
    public const double BeltSpeed = 0.5;

    /// <summary>Half the width of the pusher's face plate along the belt. While
    /// the plate is across the lane it is an obstruction of this size.</summary>
    public const double PlateHalfWidth = 0.17;

    /// <summary>
    /// Belt transport speed, in m/s. Separate from the <see cref="BeltSpeed"/>
    /// default so the conveyor part's speed property actually drives the
    /// simulation — otherwise changing it in the inspector only altered how fast
    /// the tread texture scrolled.
    /// </summary>
    public double TransportSpeed { get; set; } = BeltSpeed;
    public const double PusherTravelTime = 0.3;
    public const double PusherCatch = 0.15;

    /// <summary>Grid lane the diverter and chute sit on, one cell either side of
    /// the belt centre line. The pusher is at -ChuteLane, the chute at +ChuteLane.</summary>
    public const double ChuteLane = 0.5;

    /// <summary>How far a diverted box travels down the chute before it is
    /// counted and removed.</summary>
    public const double ChuteTravel = 0.55;

    public const double ShortHeight = 0.1;
    public const double TallHeight = 0.3;

    public sealed class Box
    {
        public double Height;
        public double Position;
        public double ChutePosition;
        public bool IsDiverted;
        public bool IsTall => Height > (ShortHeight + TallHeight) / 2;
    }

    public string Name => "sorting-by-height";
    public TagTable Tags { get; } = new();

    public readonly List<Box> Boxes = new();
    public readonly List<Box> SortedTall = new();
    public readonly List<Box> SortedShort = new();

    /// <summary>True emits a tall box. Cycled, so one pass exercises both branches.</summary>
    public List<bool> EmitPattern = new() { false, true };

    private int _emitIndex;
    private bool _emitEdge;
    private double _pusherExtension;

    public SortingScene()
    {
        SortingTags.Declare(Tags);
    }

    /// <summary>
    /// Return the line to its start state: no boxes anywhere, counters back to
    /// zero, pusher home. The TagTable instance is kept — replacing it would
    /// bump the bus epoch and force every connected driver to re-handshake,
    /// which is not what "reset the scene" should mean to a PLC.
    /// </summary>
    public void Reset()
    {
        Boxes.Clear();
        SortedTall.Clear();
        SortedShort.Clear();
        _emitIndex = 0;
        _emitEdge = false;
        _pusherExtension = 0.0;

        Tags.Set(SortingTags.CounterTall, 0);
        Tags.Set(SortingTags.CounterShort, 0);
        Tags.Set(SortingTags.SensorLowDetect, false);
        Tags.Set(SortingTags.SensorHighDetect, false);
        Tags.Set(SortingTags.PusherExtended, false);
        Tags.Set(SortingTags.PusherRetracted, true);
    }

    public void Tick(double dt)
    {
        StepEmitter();
        StepBelt(dt);
        StepPusher(dt);
        StepSensors();
        StepCounters();
    }

    private void StepEmitter()
    {
        bool emit = (bool)Tags.Visible("emitter.emit");
        if (emit && !_emitEdge)
        {
            bool tall = EmitPattern[_emitIndex % EmitPattern.Count];
            _emitIndex++;
            Boxes.Add(new Box { Height = tall ? TallHeight : ShortHeight, Position = EmitterPos });
        }
        _emitEdge = emit;
    }

    private void StepBelt(double dt)
    {
        if (!(bool)Tags.Visible("conveyor.rotate")) return;

        // A held-out pusher is a wall across the lane, not a magic diverter:
        // boxes that were not swept by the stroke pile up behind its face plate.
        double plateFace = PusherPos - PlateHalfWidth - BoxLength / 2;
        bool plateAcross = _pusherExtension >= 0.5;

        // Front box first, so each one can be stopped by whatever is ahead of it.
        // Boxes are appended as they are emitted, so index 0 is furthest along.
        double queueTail = double.PositiveInfinity;

        for (int i = 0; i < Boxes.Count; i++)
        {
            var box = Boxes[i];
            if (box.IsDiverted) continue;

            double advanced = box.Position + TransportSpeed * dt;

            // Cartons on a belt do not interpenetrate; they accumulate. Boxes
            // are in emission order, so this one is always behind the last.
            advanced = System.Math.Min(advanced, queueTail);

            // The test is against the plate's *centre*, not its face: a box held
            // at the face is still upstream and must stay held. Comparing with
            // the face let every blocked box slip through on the next tick, once
            // its position was exactly equal to it.
            if (plateAcross && box.Position < PusherPos)
                advanced = System.Math.Min(advanced, plateFace);

            box.Position = advanced;
            queueTail = box.Position - BoxLength;
        }

        for (int i = Boxes.Count - 1; i >= 0; i--)
        {
            var box = Boxes[i];
            if (!box.IsDiverted)
            {
                if (box.Position >= RemoverPos)
                {
                    SortedShort.Add(box);
                    Boxes.RemoveAt(i);
                }
            }
            else
            {
                box.ChutePosition += TransportSpeed * 0.8 * dt;
                if (box.ChutePosition >= ChuteTravel)
                {
                    SortedTall.Add(box);
                    Boxes.RemoveAt(i);
                }
            }
        }
    }

    private void StepPusher(double dt)
    {
        double previous = _pusherExtension;
        double target = (bool)Tags.Visible("pusher.extend") ? 1.0 : 0.0;
        double step = dt / PusherTravelTime;
        if (_pusherExtension < target) _pusherExtension = System.Math.Min(target, _pusherExtension + step);
        else if (_pusherExtension > target) _pusherExtension = System.Math.Max(target, _pusherExtension - step);

        Tags.Set("pusher.extended", _pusherExtension >= 1.0);
        Tags.Set("pusher.retracted", _pusherExtension <= 0.0);

        // Only the *stroke* diverts. A plate that is already across the lane
        // cannot shove anything sideways — it just stands in the way, which is
        // what StepBelt treats it as. Holding the pusher out therefore jams the
        // line instead of teleporting every arriving box onto the chute.
        bool stroking = _pusherExtension > previous;
        if (!stroking) return;

        for (int i = 0; i < Boxes.Count; i++)
        {
            var box = Boxes[i];
            if (!box.IsDiverted && System.Math.Abs(box.Position - PusherPos) <= PusherCatch)
            {
                box.IsDiverted = true;
            }
        }
    }

    private void StepSensors()
    {
        Tags.Set("sensor_low.detect", Occupied(SensorLowPos, 0.0));
        Tags.Set("sensor_high.detect", Occupied(SensorHighPos, TallHeight));
    }

    private bool Occupied(double position, double minHeight)
    {
        double half = SensorWindow / 2;
        foreach (var box in Boxes)
            if (System.Math.Abs(box.Position - position) <= half && box.Height >= minHeight)
                return true;
        return false;
    }

    private void StepCounters()
    {
        Tags.Set("counter.tall", SortedTall.Count);
        Tags.Set("counter.short", SortedShort.Count);
    }
}
