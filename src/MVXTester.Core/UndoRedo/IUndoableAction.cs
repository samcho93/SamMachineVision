namespace MVXTester.Core.UndoRedo;

public interface IUndoableAction
{
    string Description { get; }
    void Execute();
    void Undo();
}
