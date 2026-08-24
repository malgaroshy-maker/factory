using System.IO;
using Godot;

namespace FactoryForge.Editor;

/// <summary>How the sidecar was found, because the two kinds are launched
/// differently: a frozen executable runs itself, a source package needs a
/// Python to run it.</summary>
public enum SidecarKind
{
    /// <summary>A PyInstaller build shipped beside the engine. No Python on the
    /// machine required, which is the whole point of a release.</summary>
    Bundled,

    /// <summary>The <c>sidecar/</c> package in a checkout, run with `python -m`.</summary>
    Source,
}

/// <summary>Where the sidecar is and how to start it.</summary>
public readonly record struct SidecarLocation(string Path, SidecarKind Kind)
{
    /// <summary>The directory to run from.</summary>
    public string WorkingDir =>
        Kind == SidecarKind.Bundled ? System.IO.Path.GetDirectoryName(Path) ?? "." : Path;

    /// <summary>The command line, given the sidecar's own arguments. A frozen
    /// build is the executable; a checkout needs the interpreter in front.</summary>
    public string CommandFor(string arguments) =>
        Kind == SidecarKind.Bundled
            ? $"\"{Path}\" {arguments}"
            : TerminalLauncher.PythonCommand(arguments);
}

/// <summary>
/// Find the Python sidecar, in a checkout <em>and</em> beside a shipped binary
/// (UX-04).
///
/// The old rule was one line: take the parent of
/// <c>ProjectSettings.GlobalizePath("res://")</c> and look for <c>sidecar/</c>
/// in it. That is right in a checkout, where <c>res://</c> is <c>engine/</c>
/// and the sidecar sits beside it — and wrong in an export, where
/// <c>res://</c> resolves to the executable's own directory, so it searched the
/// *parent of the install folder* and missed. F5's "command copied, run it
/// yourself" then became the normal path rather than the exception, which is a
/// poor first experience for someone who downloaded a binary precisely so they
/// would not have to know where anything lives.
/// </summary>
public static class SidecarLocator
{
    /// <summary>Environment variable to override the search outright.</summary>
    public const string OverrideVar = "FACTORYFORGE_SIDECAR";

    private static string ExeName =>
        OS.GetName() == "Windows" ? "factoryforge-sidecar.exe" : "factoryforge-sidecar";

    /// <summary>Where the sidecar is, or null with nowhere left to look.</summary>
    public static SidecarLocation? Find()
    {
        foreach (string dir in SearchDirs())
        {
            if (dir.Length == 0) continue;

            // A frozen build wins over a source tree in the same place: if a
            // release ships both, the one that needs no Python is the one to
            // run.
            string exe = Path.Combine(dir, ExeName);
            if (File.Exists(exe)) return new SidecarLocation(exe, SidecarKind.Bundled);

            if (Directory.Exists(Path.Combine(dir, "factoryforge_sidecar")))
                return new SidecarLocation(dir, SidecarKind.Source);
        }
        return null;
    }

    /// <summary>Every directory checked, in order, so a failure can say where
    /// it looked rather than only that it failed.</summary>
    public static string[] SearchDirs()
    {
        string res = ProjectSettings.GlobalizePath("res://").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? parent = Path.GetDirectoryName(res);
        string exeDir = OS.GetExecutablePath().GetBaseDir();

        return new[]
        {
            OS.GetEnvironment(OverrideVar),                        // explicit wins
            res,                                                   // beside the binary, in a release
            Path.Combine(res, "sidecar"),
            parent is null ? "" : Path.Combine(parent, "sidecar"), // beside engine/, in a checkout
            exeDir,
            Path.Combine(exeDir, "sidecar"),
        };
    }
}
