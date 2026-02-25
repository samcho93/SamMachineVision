using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

public enum ComparisonOp
{
    GreaterThan,
    LessThan,
    GreaterOrEqual,
    LessOrEqual,
    Equal,
    NotEqual
}

[NodeInfo("Comparison", NodeCategories.Value, Description = "Compare two numeric values")]
public class ComparisonNode : BaseNode
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
        _operator = AddEnumProperty("Operator", "Operator", ComparisonOp.GreaterThan, "Comparison operator");
    }

    public override void Process()
    {
        try
        {
            var a = GetInputValue(_aInput);
            var b = GetInputValue(_bInput);
            var op = _operator.GetValue<ComparisonOp>();

            bool result = op switch
            {
                ComparisonOp.GreaterThan => a > b,
                ComparisonOp.LessThan => a < b,
                ComparisonOp.GreaterOrEqual => a >= b,
                ComparisonOp.LessOrEqual => a <= b,
                ComparisonOp.Equal => Math.Abs(a - b) < double.Epsilon,
                ComparisonOp.NotEqual => Math.Abs(a - b) >= double.Epsilon,
                _ => false
            };

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Comparison error: {ex.Message}";
        }
    }
}
