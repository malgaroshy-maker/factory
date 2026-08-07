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
    public const double PusherTravelTime = 0.3;
    public const double PusherCatch = 0.15;

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
        // PLC outputs — the program writes these.
        Tags.Add(new Tag("conveyor.rotate", "Belt Conveyor (Rotate)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("emitter.emit", "Emitter (Emit)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("pusher.extend", "Pusher (Extend)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("stack_light.green", "Stack Light (Green)", TagType.Bit, TagKind.Output));
        // PLC inputs — the simulator writes these.
        Tags.Add(new Tag("sensor_low.detect", "Diffuse Sensor Low (Detect)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("sensor_high.detect", "Diffuse Sensor High (Detect)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("pusher.extended", "Pusher (Extended)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("pusher.retracted", "Pusher (Retracted)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("counter.tall", "Counter (Tall)", TagType.Int, TagKind.Input));
        Tags.Add(new Tag("counter.short", "Counter (Short)", TagType.Int, TagKind.Input));

        Tags.Set("pusher.retracted", true);
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

        for (int i = Boxes.Count - 1; i >= 0; i--)
        {
            var box = Boxes[i];
            if (!box.IsDiverted)
            {
                box.Position += BeltSpeed * dt;
                if (box.Position >= RemoverPos)
                {
                    SortedShort.Add(box);
                    Boxes.RemoveAt(i);
                }
            }
            else
            {
                box.ChutePosition += BeltSpeed * 0.8 * dt;
                if (box.ChutePosition >= 0.55)
                {
                    SortedTall.Add(box);
                    Boxes.RemoveAt(i);
                }
            }
        }
    }

    private void StepPusher(double dt)
    {
        double target = (bool)Tags.Visible("pusher.extend") ? 1.0 : 0.0;
        double step = dt / PusherTravelTime;
        if (_pusherExtension < target) _pusherExtension = System.Math.Min(target, _pusherExtension + step);
        else if (_pusherExtension > target) _pusherExtension = System.Math.Max(target, _pusherExtension - step);

        Tags.Set("pusher.extended", _pusherExtension >= 1.0);
        Tags.Set("pusher.retracted", _pusherExtension <= 0.0);

        if (_pusherExtension < 0.5) return;
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
