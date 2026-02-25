using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

public enum CompareOperator
{
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Equal,
    NotEqual
}

[NodeInfo("Compare", NodeCategories.Control, Description = "Compare two numeric values")]
public class CompareNode : BaseNode
{
    private InputPort<double> _aInput = null!;
    private InputPort<double> _bInput = null!;
    private OutputPort<bool> _resultOutput = null!;
    private NodeProperty _operator = null!;

    protected override void Setup()
    {
        _aInput = AddInput<double>("A");
        _bInput = AddInput<double>("B");
        _resultOutput = AddOutput<bool>("Result");
        _operator = AddEnumProperty("Operator", "Operator", CompareOperator.Equal, "Comparison operator");
    }

    public override void Process()
    {
        try
        {
            var a = GetInputValue(_aInput);
            var b = GetInputValue(_bInput);
            var op = _operator.GetValue<CompareOperator>();

            bool result = op switch
            {
                CompareOperator.GreaterThan => a > b,
                CompareOperator.LessThan => a < b,
                CompareOperator.GreaterOrEqual => a >= b,
                CompareOperator.LessOrEqual => a <= b,
                CompareOperator.Equal => Math.Abs(a - b) < double.Epsilon,
                CompareOperator.NotEqual => Math.Abs(a - b) >= double.Epsilon,
                _ => false
            };

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Compare error: {ex.Message}";
        }
    }
}
