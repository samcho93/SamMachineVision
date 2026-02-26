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
            var trueConnected = _trueInput.IsConnected;
            var falseConnected = _falseInput.IsConnected;

            // Only True connected → pass-through True Value (no selection)
            if (trueConnected && !falseConnected)
            {
                SetOutputValue(_resultOutput, GetInputValue(_trueInput));
            }
            // Only False connected → pass-through False Value (no selection)
            else if (!trueConnected && falseConnected)
            {
                SetOutputValue(_resultOutput, GetInputValue(_falseInput));
            }
            // Both connected → select based on Condition
            else if (trueConnected && falseConnected)
            {
                var condition = GetInputValue(_conditionInput);
                var result = condition ? GetInputValue(_trueInput) : GetInputValue(_falseInput);
                SetOutputValue(_resultOutput, result);
            }
            else
            {
                SetOutputValue(_resultOutput, (object?)null);
            }

            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"If Select error: {ex.Message}";
        }
    }
}
