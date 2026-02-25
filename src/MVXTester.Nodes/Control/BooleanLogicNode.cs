using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

public enum BoolLogicGate
{
    AND,
    OR,
    XOR,
    NAND,
    NOR,
    NOT_A
}

[NodeInfo("Boolean Logic", NodeCategories.Control, Description = "Boolean logic operations for control flow")]
public class BooleanLogicNode : BaseNode
{
    private InputPort<bool> _aInput = null!;
    private InputPort<bool> _bInput = null!;
    private OutputPort<bool> _resultOutput = null!;
    private NodeProperty _gate = null!;

    protected override void Setup()
    {
        _aInput = AddInput<bool>("A");
        _bInput = AddInput<bool>("B");
        _resultOutput = AddOutput<bool>("Result");
        _gate = AddEnumProperty("Gate", "Gate", BoolLogicGate.AND, "Logic gate type");
    }

    public override void Process()
    {
        try
        {
            var a = GetInputValue(_aInput);
            var b = GetInputValue(_bInput);
            var gate = _gate.GetValue<BoolLogicGate>();

            bool result = gate switch
            {
                BoolLogicGate.AND => a && b,
                BoolLogicGate.OR => a || b,
                BoolLogicGate.XOR => a ^ b,
                BoolLogicGate.NAND => !(a && b),
                BoolLogicGate.NOR => !(a || b),
                BoolLogicGate.NOT_A => !a,
                _ => false
            };

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Boolean Logic error: {ex.Message}";
        }
    }
}
