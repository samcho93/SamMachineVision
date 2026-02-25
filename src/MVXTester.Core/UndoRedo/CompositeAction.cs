namespace MVXTester.Core.UndoRedo;

public class CompositeAction : IUndoableAction
{
    private readonly List<IUndoableAction> _actions;
    public string Description { get; }

    public CompositeAction(string description, List<IUndoableAction> actions)
    {
        Description = description;
        _actions = actions;
    }

    public void Execute()
    {
        foreach (var action in _actions)
            action.Execute();
    }

    public void Undo()
    {
        for (int i = _actions.Count - 1; i >= 0; i--)
            _actions[i].Undo();
    }
}
