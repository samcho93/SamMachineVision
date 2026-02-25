using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

[NodeInfo("Boolean", NodeCategories.Control, Description = "Boolean constant for control flow")]
public class BooleanNode : BaseNode
{
    private OutputPort<bool> _valueOutput = null!;
    private NodeProperty _value = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<bool>("Value");
        _value = AddBoolProperty("Value", "Value", false, "Boolean value");
    }

    public override void Process()
    {
        SetOutputValue(_valueOutput, _value.GetValue<bool>());
        Error = null;
    }
}
