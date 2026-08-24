using System.Collections.Generic;
using FactoryForge.Editor;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Check the engine can find a sidecar to launch (UX-04).
///
/// <code>godot --headless --path engine -- --self-test=sidecar</code>
///
/// Worth its own check because the failure is invisible from inside the
/// engine: F5 falls back to "command copied, run it yourself", which looks
/// like a deliberate feature rather than a search that missed. In a packaged
/// build that fallback used to be the *only* path, because
/// <c>res://</c> resolves to the executable's own directory in an export and
/// the old code looked in its parent.
///
/// Run this against a release binary as well as a checkout -- it reports which
/// kind it found, and the two answers should differ: Source in a checkout,
/// Bundled beside a shipped binary.
/// </summary>
public partial class SidecarLocatorSelfTest : Node
{
    private readonly List<string> _failures = new();

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _Ready()
    {
        GD.Print("  searched, in order:");
        foreach (string dir in SidecarLocator.SearchDirs())
            GD.Print($"    {(dir.Length == 0 ? "(unset)" : dir)}");

        var found = SidecarLocator.Find();
        Expect(found is not null,
               "a sidecar was found -- without one, F5 can only copy a command to the clipboard");

        if (found is { } sidecar)
        {
            GD.Print($"  found: {sidecar.Kind} at {sidecar.Path}");
            GD.Print($"  would run: cd \"{sidecar.WorkingDir}\" && {sidecar.CommandFor("connect --driver mock")}");

            Expect(sidecar.WorkingDir.Length > 0, "the located sidecar has a working directory");
            Expect(sidecar.CommandFor("connect").Contains("connect"),
                   "the command carries the sidecar's own arguments");
            // A frozen build must not be prefixed with an interpreter, and a
            // source checkout must be: getting this backwards produces a
            // command that looks plausible and cannot run.
            if (sidecar.Kind == SidecarKind.Bundled)
                Expect(!sidecar.CommandFor("connect").StartsWith("python"),
                       "a bundled sidecar runs itself, with no interpreter in front");
            else
                Expect(sidecar.CommandFor("connect").StartsWith("python"),
                       "a source sidecar is run through an interpreter");
        }

        if (_failures.Count == 0)
        {
            GD.Print("self-test sidecar: PASS");
            GetTree().Quit(0);
            return;
        }
        GD.PrintErr($"self-test sidecar: FAIL ({_failures.Count})");
        GetTree().Quit(1);
    }
}
