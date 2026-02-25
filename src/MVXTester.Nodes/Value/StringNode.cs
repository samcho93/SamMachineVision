using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("String", NodeCategories.Value, Description = "String constant value")]
public class StringNode : BaseNode
{
    private OutputPort<string> _valueOutput = null!;
    private NodeProperty _value = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<string>("Value");
        _value = AddStringProperty("Value", "Value", "", "String value");
    }

    public override void Process()
    {
        SetOutputValue(_valueOutput, _value.GetValue<string>());
        Error = null;
    }
}
