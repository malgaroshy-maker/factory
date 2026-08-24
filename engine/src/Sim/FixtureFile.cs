using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Reads a checked-in test fixture, in a checkout <em>and</em> in an exported
/// binary (UX-08).
///
/// Two things made this necessary. The fixtures used to live in
/// <c>tests/fixtures/</c> and be reached as
/// <c>ProjectSettings.GlobalizePath("res://") + "/../tests/fixtures/..."</c> --
/// a path outside the project, which simply does not exist beside an exported
/// executable. And even once they moved under <c>res://</c>, an export packs
/// them <em>inside</em> the .pck, where <see cref="System.IO.File"/> cannot
/// reach them at all: only Godot's own <see cref="FileAccess"/> reads through
/// the virtual filesystem.
///
/// So this is not a tidy-up. Either mistake alone makes every fixture-backed
/// self-test fail the moment it runs against a real release build, which is
/// exactly when a release most needs its self-tests to work.
/// </summary>
public static class FixtureFile
{
    /// <summary>Read a <c>res://</c> file as text, or throw saying which one
    /// and where it was looked for.</summary>
    public static string Read(string resPath)
    {
        using var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            throw new System.IO.FileNotFoundException(
                $"fixture '{resPath}' is missing. In a checkout it lives under engine/fixtures/; " +
                $"in an export it must be packed into the .pck -- check export_presets.cfg's " +
                $"exclude_filter. Godot reported: {FileAccess.GetOpenError()}", resPath);
        }
        return file.GetAsText();
    }
}
