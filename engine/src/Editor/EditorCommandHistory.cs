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
