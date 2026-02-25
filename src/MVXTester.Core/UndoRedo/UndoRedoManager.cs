namespace MVXTester.Core.UndoRedo;

public class UndoRedoManager
{
    private readonly Stack<IUndoableAction> _undoStack = new();
    private readonly Stack<IUndoableAction> _redoStack = new();
    private readonly int _maxHistorySize;
    private bool _isExecutingUndoRedo;

    public event Action? StateChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;
    public bool IsExecutingUndoRedo => _isExecutingUndoRedo;

    public UndoRedoManager(int maxHistorySize = 100)
    {
        _maxHistorySize = maxHistorySize;
    }

    public void ExecuteAction(IUndoableAction action)
    {
        if (_isExecutingUndoRedo) return;
        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear();
        TrimHistory();
        StateChanged?.Invoke();
    }

    public void PushAction(IUndoableAction action)
    {
        if (_isExecutingUndoRedo) return;
        _undoStack.Push(action);
        _redoStack.Clear();
        TrimHistory();
        StateChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _isExecutingUndoRedo = true;
        try
        {
            var action = _undoStack.Pop();
            action.Undo();
            _redoStack.Push(action);
            StateChanged?.Invoke();
        }
        finally
        {
            _isExecutingUndoRedo = false;
        }
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _isExecutingUndoRedo = true;
        try
        {
            var action = _redoStack.Pop();
            action.Execute();
            _undoStack.Push(action);
            StateChanged?.Invoke();
        }
        finally
        {
            _isExecutingUndoRedo = false;
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
    }

    public IUndoableAction? PeekUndo() => _undoStack.Count > 0 ? _undoStack.Peek() : null;

    private void TrimHistory()
    {
        if (_undoStack.Count > _maxHistorySize)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = Math.Min(items.Length - 1, _maxHistorySize - 1); i >= 0; i--)
                _undoStack.Push(items[i]);
        }
    }
}
