using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

public enum LogicGate
{
    AND,
    OR,
    XOR,
    NAND,
    NOR
}

[NodeInfo("Logic Gate", NodeCategories.Value, Description = "Boolean logic gate operation")]
public class LogicGateNode : BaseNode
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
        _gate = AddEnumProperty("Gate", "Gate", LogicGate.AND, "Logic gate type");
    }

    public override void Process()
    {
        try
        {
            var a = GetInputValue(_aInput);
            var b = GetInputValue(_bInput);
            var gate = _gate.GetValue<LogicGate>();

            bool result = gate switch
            {
                LogicGate.AND => a && b,
                LogicGate.OR => a || b,
                LogicGate.XOR => a ^ b,
                LogicGate.NAND => !(a && b),
                LogicGate.NOR => !(a || b),
                _ => false
            };

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Logic Gate error: {ex.Message}";
        }
    }
}
