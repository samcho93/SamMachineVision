using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Float", NodeCategories.Value, Description = "Float constant value")]
public class FloatNode : BaseNode
{
    private OutputPort<float> _valueOutput = null!;
    private NodeProperty _value = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<float>("Value");
        _value = AddFloatProperty("Value", "Value", 0f, float.MinValue, float.MaxValue, "Float value");
    }

    public override void Process()
    {
        SetOutputValue(_valueOutput, _value.GetValue<float>());
        Error = null;
    }
}
