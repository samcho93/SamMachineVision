using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

public enum MathOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Power,
    Min,
    Max
}

[NodeInfo("Math Operation", NodeCategories.Value, Description = "Perform math operation on two values")]
public class MathOperationNode : BaseNode
{
    private InputPort<double> _aInput = null!;
    private InputPort<double> _bInput = null!;
    private OutputPort<double> _resultOutput = null!;
    private NodeProperty _operation = null!;

    protected override void Setup()
    {
        _aInput = AddInput<double>("A");
        _bInput = AddInput<double>("B");
        _resultOutput = AddOutput<double>("Result");
        _operation = AddEnumProperty("Operation", "Operation", MathOp.Add, "Math operation");
    }

    public override void Process()
    {
        try
        {
            var a = GetInputValue(_aInput);
            var b = GetInputValue(_bInput);
            var op = _operation.GetValue<MathOp>();

            double result = op switch
            {
                MathOp.Add => a + b,
                MathOp.Subtract => a - b,
                MathOp.Multiply => a * b,
                MathOp.Divide => b != 0 ? a / b : 0,
                MathOp.Modulo => b != 0 ? a % b : 0,
                MathOp.Power => Math.Pow(a, b),
                MathOp.Min => Math.Min(a, b),
                MathOp.Max => Math.Max(a, b),
                _ => 0
            };

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Math Operation error: {ex.Message}";
        }
    }
}
