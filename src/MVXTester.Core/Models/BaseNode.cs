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

    protected void SetPreview(Mat? mat)
    {
        PreviewMat?.Dispose();
        if (mat != null && !mat.Empty())
        {
            PreviewMat = mat.Clone();
        }
        else
        {
            PreviewMat = null;
        }
    }

    protected T? GetInputValue<T>(InputPort<T> port)
    {
        return port.TypedValue;
    }

    protected void SetOutputValue<T>(OutputPort<T> port, T? value)
    {
        port.TypedValue = value;
    }
}
