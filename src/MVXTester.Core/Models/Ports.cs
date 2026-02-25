namespace MVXTester.Core.Models;

public class InputPort<T> : IInputPort
{
    public string Name { get; }
    public Type DataType => typeof(T);
    public INode Owner { get; }
    public IConnection? Connection { get; set; }
    public bool IsConnected => Connection != null;

    public InputPort(string name, INode owner)
    {
        Name = name;
        Owner = owner;
    }

    public object? GetValue()
    {
        return Connection?.Source.GetValue();
    }

    public T? TypedValue => GetValue() is T val ? val : default;
}

public class OutputPort<T> : IOutputPort
{
    public string Name { get; }
    public Type DataType => typeof(T);
    public INode Owner { get; }
    public List<IConnection> Connections { get; } = new();
    public T? TypedValue { get; set; }

    public OutputPort(string name, INode owner)
    {
        Name = name;
        Owner = owner;
    }

    public object? GetValue() => TypedValue;
    public void SetValue(object? value) => TypedValue = value is T typed ? typed : default;
}
