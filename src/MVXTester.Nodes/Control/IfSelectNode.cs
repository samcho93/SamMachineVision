using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

[NodeInfo("If Select", NodeCategories.Control, Description = "Select between two values based on a condition")]
public class IfSelectNode : BaseNode
{
    private InputPort<bool> _conditionInput = null!;
    private InputPort<object> _trueInput = null!;
    private InputPort<object> _falseInput = null!;
    private OutputPort<object> _resultOutput = null!;

    protected override void Setup()
    {
        _conditionInput = AddInput<bool>("Condition");
        _trueInput = AddInput<object>("True Value");
        _falseInput = AddInput<object>("False Value");
        _resultOutput = AddOutput<object>("Result");
    }

    public override void Process()
    {
        try
        {
            var condition = GetInputValue(_conditionInput);
            var trueValue = GetInputValue(_trueInput);
            var falseValue = GetInputValue(_falseInput);

            var result = condition ? trueValue : falseValue;
            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"If Select error: {ex.Message}";
        }
    }
}
