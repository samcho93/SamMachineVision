using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Double", NodeCategories.Value, Description = "Double constant value")]
public class DoubleNode : BaseNode
{
    private OutputPort<double> _valueOutput = null!;
    private NodeProperty _value = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<double>("Value");
        _value = AddDoubleProperty("Value", "Value", 0.0, double.MinValue, double.MaxValue, "Double value");
    }

    public override void Process()
    {
        SetOutputValue(_valueOutput, _value.GetValue<double>());
        Error = null;
    }
}
