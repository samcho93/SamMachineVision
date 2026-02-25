using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Scalar", NodeCategories.Value, Description = "Scalar value (4 components)")]
public class ScalarNode : BaseNode
{
    private OutputPort<Scalar> _valueOutput = null!;
    private NodeProperty _v0 = null!;
    private NodeProperty _v1 = null!;
    private NodeProperty _v2 = null!;
    private NodeProperty _v3 = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<Scalar>("Scalar");
        _v0 = AddDoubleProperty("V0", "V0", 0.0, 0.0, 255.0, "Component 0 (Blue)");
        _v1 = AddDoubleProperty("V1", "V1", 0.0, 0.0, 255.0, "Component 1 (Green)");
        _v2 = AddDoubleProperty("V2", "V2", 0.0, 0.0, 255.0, "Component 2 (Red)");
        _v3 = AddDoubleProperty("V3", "V3", 0.0, 0.0, 255.0, "Component 3 (Alpha)");
    }

    public override void Process()
    {
        var scalar = new Scalar(
            _v0.GetValue<double>(),
            _v1.GetValue<double>(),
            _v2.GetValue<double>(),
            _v3.GetValue<double>()
        );
        SetOutputValue(_valueOutput, scalar);
        Error = null;
    }
}
