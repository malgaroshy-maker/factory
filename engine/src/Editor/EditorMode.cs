namespace FactoryForge.Editor;

/// <summary>
/// What a click in the viewport means.
///
/// The two meanings are genuinely incompatible: in a factory you click a part to
/// pick it up and move it, and you click a button to press it. Same mouse, same
/// ray, opposite intent. Without a mode, pressing Start would either be
/// impossible or would silently select and drag the panel it lives on.
/// </summary>
public enum EditorMode
{
    /// <summary>Build the line: place, select, move and delete parts.</summary>
    Edit,

    /// <summary>Operate the line: the parts are furniture and the only things
    /// that answer a click are the controls a real operator could reach.</summary>
    Run,
}
