using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Integer", NodeCategories.Value, Description = "Integer constant value")]
public class IntegerNode : BaseNode
{
    private OutputPort<int> _valueOutput = null!;
    private NodeProperty _value = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<int>("Value");
        _value = AddIntProperty("Value", "Value", 0, int.MinValue, int.MaxValue, "Integer value");
    }

    public override void Process()
    {
        SetOutputValue(_valueOutput, _value.GetValue<int>());
        Error = null;
    }
}
