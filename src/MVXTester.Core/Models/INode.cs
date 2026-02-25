using OpenCvSharp;

namespace MVXTester.Core.Models;

public interface IInputPort
{
    string Name { get; }
    Type DataType { get; }
    INode Owner { get; }
    IConnection? Connection { get; set; }
    bool IsConnected { get; }
    object? GetValue();
}

public interface IOutputPort
{
    string Name { get; }
    Type DataType { get; }
    INode Owner { get; }
    List<IConnection> Connections { get; }
    object? GetValue();
    void SetValue(object? value);
}

public interface IConnection
{
    IOutputPort Source { get; }
    IInputPort Target { get; }
}

public interface INode
{
    string Id { get; }
    string Name { get; }
    string Category { get; }
    string Description { get; }
    IReadOnlyList<IInputPort> Inputs { get; }
    IReadOnlyList<IOutputPort> Outputs { get; }
    IReadOnlyList<NodeProperty> Properties { get; }
    bool IsDirty { get; set; }
    string? Error { get; set; }
    Mat? PreviewMat { get; }
    void Process();
}

/// <summary>
/// Marker interface for nodes that produce new data on every execution (e.g., Camera, Video).
/// The continuous execution loop will always mark these nodes dirty.
/// </summary>
public interface IStreamingSource { }

/// <summary>
/// Interface for nodes that can receive mouse events from the execution output window.
/// </summary>
public interface IMouseEventReceiver
{
    void OnMouseEvent(MouseEventData eventData);
}

/// <summary>
/// Interface for nodes that can receive keyboard events from the execution output window.
/// </summary>
public interface IKeyboardEventReceiver
{
    void OnKeyboardEvent(KeyboardEventData eventData);
}

public class MouseEventData
{
    public MouseEventType EventType { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Button { get; set; } // 0=Left, 1=Middle, 2=Right
    public int Delta { get; set; } // Scroll delta
}

public enum MouseEventType
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    MiddleDown,
    MiddleUp,
    Wheel
}

public class KeyboardEventData
{
    public KeyEventType EventType { get; set; }
    public int KeyCode { get; set; }
    public string KeyName { get; set; } = "";
    public bool IsCtrl { get; set; }
    public bool IsShift { get; set; }
    public bool IsAlt { get; set; }
}

public enum KeyEventType
{
    KeyDown,
    KeyUp
}

public enum PropertyType
{
    Integer,
    Float,
    Double,
    Boolean,
    String,
    Enum,
    Point,
    Size,
    Scalar,
    FilePath,
    Range,
    MultilineString
}

public class NodeProperty
{
    public string Name { get; }
    public string DisplayName { get; }
    public PropertyType PropertyType { get; }
    public Type ValueType { get; }
    public object? Value { get; set; }
    public object? MinValue { get; }
    public object? MaxValue { get; }
    public object? DefaultValue { get; }
    public string? Description { get; }
    public Type? EnumType { get; }

    public NodeProperty(string name, string displayName, PropertyType propertyType, Type valueType,
        object? defaultValue = null, object? minValue = null, object? maxValue = null,
        string? description = null, Type? enumType = null)
    {
        Name = name;
        DisplayName = displayName;
        PropertyType = propertyType;
        ValueType = valueType;
        DefaultValue = defaultValue;
        Value = defaultValue;
        MinValue = minValue;
        MaxValue = maxValue;
        Description = description;
        EnumType = enumType;
    }

    public event Action? ValueChanged;

    public void SetValue(object? value)
    {
        if (!Equals(Value, value))
        {
            Value = value;
            ValueChanged?.Invoke();
        }
    }

    public T GetValue<T>()
    {
        if (Value is T typed)
            return typed;
        try
        {
            return (T)Convert.ChangeType(Value!, typeof(T));
        }
        catch
        {
            return (T)DefaultValue!;
        }
    }
}
