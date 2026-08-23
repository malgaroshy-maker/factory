using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Open a visible terminal running a command in a working directory. Shared by
/// every toolbar affordance that hands a Python command to the user rather
/// than running it invisibly — F5's <em>Apply &amp; Connect</em> (the sidecar)
/// and the toolbar's <em>Try this scene</em> (UX-31, <c>tools/try_scene.py</c>)
/// both want the same thing: a console the user can watch, not a background
/// process whose only sign of life is a status label.
/// </summary>
public static class TerminalLauncher
{
    /// <summary>Windows has no <c>python3</c>; every other platform this ships
    /// for treats <c>python</c> as Python 2 or nothing at all. One place to
    /// get that right rather than each caller guessing.</summary>
    public static string PythonCommand(string arguments) =>
        (OS.GetName() == "Windows" ? "python " : "python3 ") + arguments;

    /// <summary>
    /// Windows always has cmd.exe. Linux and macOS have no equivalent
    /// guarantee — there is no single terminal binary every distro ships — so
    /// this tries a short list of common ones and lets a failed
    /// <see cref="OS.CreateProcess"/> (it returns -1, never throws) fall
    /// through to the next. Returns -1 if every candidate fails; the caller's
    /// own clipboard-fallback message is the safety net.
    /// </summary>
    public static int Spawn(string workingDir, string command)
    {
        if (OS.GetName() == "Windows")
        {
            string[] argv = { "/d", "/s", "/c", $"cd /d \"{workingDir}\" && {command}" };
            return OS.CreateProcess("cmd.exe", argv, openConsole: true);
        }

        string shellCmd = $"cd \"{workingDir}\" && {command}; exec $SHELL";

        if (OS.GetName() == "macOS")
        {
            // Terminal.app has no "run this command" flag; osascript is the
            // standard way to hand it one.
            string script = $"tell application \"Terminal\" to do script " +
                             $"\"cd '{workingDir}' && {command}\"";
            int macPid = OS.CreateProcess("osascript", new[] { "-e", script });
            if (macPid > 0) return macPid;
        }

        foreach (string terminal in new[] { "x-terminal-emulator", "gnome-terminal", "xterm" })
        {
            string[] argv = terminal == "gnome-terminal"
                ? new[] { "--", "bash", "-c", shellCmd }
                : new[] { "-e", "bash", "-c", shellCmd };
            int pid = OS.CreateProcess(terminal, argv);
            if (pid > 0) return pid;
        }
        return -1;
    }
}
