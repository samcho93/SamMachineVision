using OpenCvSharp;

namespace MVXTester.Core.Models;

[AttributeUsage(AttributeTargets.Class)]
public class NodeInfoAttribute : Attribute
{
    public string Name { get; }
    public string Category { get; }
    public string Description { get; set; } = "";

    public NodeInfoAttribute(string name, string category)
    {
        Name = name;
        Category = category;
    }
}

public abstract class BaseNode : INode
{
    private readonly List<IInputPort> _inputs = new();
    private readonly List<IOutputPort> _outputs = new();
    private readonly List<NodeProperty> _properties = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; }
    public string Category { get; }
    public string Description { get; }
    public IReadOnlyList<IInputPort> Inputs => _inputs;
    public IReadOnlyList<IOutputPort> Outputs => _outputs;
    public IReadOnlyList<NodeProperty> Properties => _properties;
    public bool IsDirty { get; set; } = true;
    public string? Error { get; set; }
    public Mat? PreviewMat { get; protected set; }
    public readonly object PreviewLock = new();

    protected BaseNode()
    {
        var attr = GetType().GetCustomAttributes(typeof(NodeInfoAttribute), false)
            .FirstOrDefault() as NodeInfoAttribute;
        Name = attr?.Name ?? GetType().Name;
        Category = attr?.Category ?? "Misc";
        Description = attr?.Description ?? "";
        Setup();
    }

    protected abstract void Setup();
    public abstract void Process();

    public virtual void Cleanup() { }

    protected InputPort<T> AddInput<T>(string name)
    {
        var port = new InputPort<T>(name, this);
        _inputs.Add(port);
        return port;
    }

    protected OutputPort<T> AddOutput<T>(string name)
    {
        var port = new OutputPort<T>(name, this);
        _outputs.Add(port);
        return port;
    }

    /// <summary>
    /// 런타임에 타입을 지정하여 입력 포트 생성 (함수 노드용).
    /// 리플렉션으로 InputPort&lt;T&gt;를 생성.
    /// </summary>
    protected IInputPort AddInputDynamic(string name, Type dataType)
    {
        var portType = typeof(InputPort<>).MakeGenericType(dataType);
        var port = (IInputPort)Activator.CreateInstance(portType, name, this)!;
        _inputs.Add(port);
        return port;
    }

    /// <summary>
    /// 런타임에 타입을 지정하여 출력 포트 생성 (함수 노드용).
    /// 리플렉션으로 OutputPort&lt;T&gt;를 생성.
    /// </summary>
    protected IOutputPort AddOutputDynamic(string name, Type dataType)
    {
        var portType = typeof(OutputPort<>).MakeGenericType(dataType);
        var port = (IOutputPort)Activator.CreateInstance(portType, name, this)!;
        _outputs.Add(port);
        return port;
    }

    protected NodeProperty AddProperty(string name, string displayName, PropertyType type,
        Type valueType, object? defaultValue = null, object? min = null, object? max = null,
        string? description = null, Type? enumType = null)
    {
        var prop = new NodeProperty(name, displayName, type, valueType, defaultValue, min, max, description, enumType);
        prop.ValueChanged += () => IsDirty = true;
        _properties.Add(prop);
        return prop;
    }

    protected NodeProperty AddIntProperty(string name, string displayName, int defaultValue = 0,
        int min = int.MinValue, int max = int.MaxValue, string? description = null)
        => AddProperty(name, displayName, PropertyType.Integer, typeof(int), defaultValue, min, max, description);

    protected NodeProperty AddFloatProperty(string name, string displayName, float defaultValue = 0f,
        float min = float.MinValue, float max = float.MaxValue, string? description = null)
        => AddProperty(name, displayName, PropertyType.Float, typeof(float), defaultValue, min, max, description);

    protected NodeProperty AddDoubleProperty(string name, string displayName, double defaultValue = 0.0,
        double min = double.MinValue, double max = double.MaxValue, string? description = null)
        => AddProperty(name, displayName, PropertyType.Double, typeof(double), defaultValue, min, max, description);

    protected NodeProperty AddBoolProperty(string name, string displayName, bool defaultValue = false,
        string? description = null)
        => AddProperty(name, displayName, PropertyType.Boolean, typeof(bool), defaultValue, description: description);

    protected NodeProperty AddStringProperty(string name, string displayName, string defaultValue = "",
        string? description = null)
        => AddProperty(name, displayName, PropertyType.String, typeof(string), defaultValue, description: description);

    protected NodeProperty AddEnumProperty<TEnum>(string name, string displayName, TEnum defaultValue,
        string? description = null) where TEnum : struct, Enum
        => AddProperty(name, displayName, PropertyType.Enum, typeof(TEnum), defaultValue,
            description: description, enumType: typeof(TEnum));

    protected NodeProperty AddFilePathProperty(string name, string displayName, string defaultValue = "",
        string? description = null)
        => AddProperty(name, displayName, PropertyType.FilePath, typeof(string), defaultValue, description: description);

    protected NodeProperty AddMultilineStringProperty(string name, string displayName, string defaultValue = "",
        string? description = null)
        => AddProperty(name, displayName, PropertyType.MultilineString, typeof(string), defaultValue, description: description);

    protected NodeProperty AddDeviceListProperty(string name, string displayName, int defaultValue = -1,
        string? description = null)
        => AddProperty(name, displayName, PropertyType.DeviceList, typeof(int), defaultValue, description: description);

    protected void SetPreview(Mat? mat)
    {
        lock (PreviewLock)
        {
            var old = PreviewMat;
            if (mat != null && !mat.Empty())
                PreviewMat = mat.Clone();
            else
                PreviewMat = null;
            try { old?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Thread-safe clone of PreviewMat. Returns null if no preview available.
    /// </summary>
    public Mat? ClonePreview()
    {
        lock (PreviewLock)
        {
            var mat = PreviewMat;
            if (mat != null && !mat.IsDisposed && !mat.Empty())
                return mat.Clone();
            return null;
        }
    }

    protected T? GetInputValue<T>(InputPort<T> port)
    {
        return port.TypedValue;
    }

    /// <summary>
    /// Returns the value from the input port if connected, otherwise falls back to the property value.
    /// Use for parameters that have both an optional input port and a manual property.
    /// </summary>
    protected T GetPortOrProperty<T>(InputPort<T> port, NodeProperty property) where T : struct
    {
        if (port.IsConnected)
            return port.GetValue() is T val ? val : property.GetValue<T>();
        return property.GetValue<T>();
    }

    /// <summary>
    /// String version of GetPortOrProperty. Returns port value if connected and non-empty, otherwise property value.
    /// </summary>
    protected string GetPortOrPropertyString(InputPort<string> port, NodeProperty property)
    {
        if (port.IsConnected)
        {
            var val = port.GetValue() as string;
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return property.GetValue<string>();
    }

    protected void SetOutputValue<T>(OutputPort<T> port, T? value)
    {
        var old = port.TypedValue;
        port.TypedValue = value;
        // Dispose previous output value to prevent memory leak during streaming
        if (old is IDisposable disposable && !ReferenceEquals(old, value))
        {
            try { disposable.Dispose(); } catch { }
        }
    }
}
