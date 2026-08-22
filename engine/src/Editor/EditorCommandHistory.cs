using System.Collections.Generic;
using Godot;

namespace FactoryForge.Editor;

public interface IEditorCommand
{
    void Execute();
    void Undo();
}

/// <summary>
/// Maintains undo/redo command history stack for Scene Editor actions (Ctrl+Z / Ctrl+Y).
/// </summary>
public class EditorCommandHistory
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();

    public void ExecuteCommand(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    /// <summary>Execute a command and make it the *only* thing left to undo,
    /// dropping whatever history came before it. For a command whose Undo
    /// rebuilds state that invalidates every earlier command's references —
    /// Scene Clear being the case this exists for, since the commands below it
    /// point at nodes the clear just freed.</summary>
    public void ExecuteAsOnly(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Clear();
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
