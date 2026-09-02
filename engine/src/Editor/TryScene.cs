using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Run <c>tools/try_scene.py</c> for whatever scene is loaded — the action
/// behind the toolbar's "Try this scene" button (UX-31) and F5's empty-state
/// offer (UX-33). One place, so the two can never resolve a different scene
/// id, or refuse with a different message, than the other.
/// </summary>
public static class TryScene
{
    /// <summary>Which manifest entry matches a loaded scene's name, or null
    /// for a custom scene with no built-in exercise.</summary>
    public static TemplateEntry? FindManifestEntry(string sceneName)
    {
        foreach (var entry in TemplateManifest.Load())
        {
            if (entry.Scene == sceneName) return entry;
        }
        return null;
    }

    /// <summary>
    /// Copy <c>python tools/try_scene.py --scene &lt;id&gt;</c> to the
    /// clipboard, print it, and open a terminal running it — or announce an
    /// honest refusal through <paramref name="idleHint"/>, naming the actual
    /// scene, if there is nothing to run (the FF-06/FF-23/UX-30 dishonesty
    /// class closed everywhere else in this plan).
    /// </summary>
    public static void Run(SceneEditor editor, IdleHintUI? idleHint)
    {
        var entry = FindManifestEntry(editor.SceneName);
        if (entry is null)
        {
            idleHint?.Announce($"No built-in exercise for scene '{editor.SceneName}' — "
                + "try one of the shipped templates instead.");
            return;
        }

        string engineDir = ProjectSettings.GlobalizePath("res://").TrimEnd('/', '\\');
        string repoRoot = System.IO.Path.GetDirectoryName(engineDir) ?? engineDir;
        string command = TerminalLauncher.PythonCommand($"tools/try_scene.py --scene {entry.Id}");
        DisplayServer.ClipboardSet(command);
        GD.Print($"try_scene.py command (copied to clipboard):\n  {command}");

        if (TerminalLauncher.Spawn(repoRoot, command) <= 0)
        {
            idleHint?.Announce("Could not start python. The command is on your clipboard — run it yourself.");
        }
    }
}
