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
    bool IsRuntimeMode { get; set; }
    string? Error { get; set; }
    Mat? PreviewMat { get; }
    Mat? ClonePreview();
    void Process();
}

/// <summary>
/// Marker interface for nodes that produce new data on every execution (e.g., Camera, Video).
/// The continuous execution loop will always mark these nodes dirty.
/// </summary>
public interface IStreamingSource { }

/// <summary>
/// Interface for loop control nodes (For, While, ForEach).
/// When GraphExecutor encounters an ILoopNode, it repeatedly executes
/// downstream "body" nodes instead of calling Process().
/// Body nodes are identified by traversing downstream connections,
/// stopping at ILoopCollector nodes (which act as loop boundaries).
/// </summary>
public interface ILoopNode
{
    /// <summary>Initialize loop state before first iteration.</summary>
    void InitializeLoop();

    /// <summary>
    /// Advance to next iteration and update output ports.
    /// Returns true if the iteration should execute, false if loop is complete.
    /// </summary>
    bool MoveNext();

    /// <summary>Called after loop completes (all iterations or break).</summary>
    void EndLoop();

    /// <summary>Maximum allowed iterations (safety limit to prevent infinite loops).</summary>
    int MaxIterations { get; }
}

/// <summary>
/// Node that accumulates values across loop iterations.
/// Acts as a loop body boundary — nodes downstream of a collector
/// are NOT part of the loop body and execute only after the loop completes.
/// </summary>
public interface ILoopCollector
{
    /// <summary>Clear accumulated data before loop starts.</summary>
    void ClearCollection();

    /// <summary>Collect current input values (called after each iteration).</summary>
    void CollectIteration();

    /// <summary>Finalize output (e.g., convert list to array) after loop ends.</summary>
    void FinalizeCollection();
}

/// <summary>
/// Node that can signal early termination of a loop.
/// GraphExecutor checks ShouldBreak after each iteration.
/// </summary>
public interface IBreakSignal
{
    bool ShouldBreak { get; }
    void ResetBreak();
}

/// <summary>
/// Interface for nodes that run background operations independently during runtime.
/// Communication nodes (Serial, TCP) implement this to continuously receive data
/// on a separate thread while the main execution loop runs.
/// StartBackground is called when runtime begins, StopBackground when it ends.
/// </summary>
public interface IBackgroundNode
{
    void StartBackground(CancellationToken ct);
    void StopBackground();
}

/// <summary>
/// Interface for nodes that support device enumeration (cameras, serial ports, etc.).
/// Used by PropertyEditorViewModel to generically refresh device lists.
/// </summary>
public interface IDeviceEnumerable
{
    void EnumerateDevices();
}

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
    MultilineString,
    DeviceList
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

    /// <summary>
    /// Option list for DeviceList property type.
    /// Each item: (DisplayName, DeviceIndex)
    /// </summary>
    public List<(string Name, int Index)> DeviceOptions { get; } = new();

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

    /// <summary>
    /// Controls whether this property is visible in the UI.
    /// Used for dynamic property visibility (e.g., CameraNode shows different properties per backend).
    /// </summary>
    public bool IsVisible { get; private set; } = true;

    public event Action? ValueChanged;
    public event Action? OptionsChanged;
    public event Action? VisibilityChanged;

    public void SetVisible(bool visible)
    {
        if (IsVisible != visible)
        {
            IsVisible = visible;
            VisibilityChanged?.Invoke();
        }
    }

    public void SetValue(object? value)
    {
        if (!Equals(Value, value))
        {
            Value = value;
            ValueChanged?.Invoke();
        }
    }

    public void UpdateDeviceOptions(List<(string Name, int Index)> options)
    {
        DeviceOptions.Clear();
        DeviceOptions.AddRange(options);
        OptionsChanged?.Invoke();
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
