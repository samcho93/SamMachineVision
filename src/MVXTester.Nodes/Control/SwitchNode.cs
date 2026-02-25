using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

[NodeInfo("Switch", NodeCategories.Control, Description = "Select one of multiple inputs by index")]
public class SwitchNode : BaseNode
{
    private InputPort<int> _indexInput = null!;
    private InputPort<object> _input0 = null!;
    private InputPort<object> _input1 = null!;
    private InputPort<object> _input2 = null!;
    private InputPort<object> _input3 = null!;
    private InputPort<object> _input4 = null!;
    private InputPort<object> _input5 = null!;
    private InputPort<object> _input6 = null!;
    private InputPort<object> _input7 = null!;
    private OutputPort<object> _resultOutput = null!;

    protected override void Setup()
    {
        _indexInput = AddInput<int>("Index");
        _input0 = AddInput<object>("Value 0");
        _input1 = AddInput<object>("Value 1");
        _input2 = AddInput<object>("Value 2");
        _input3 = AddInput<object>("Value 3");
        _input4 = AddInput<object>("Value 4");
        _input5 = AddInput<object>("Value 5");
        _input6 = AddInput<object>("Value 6");
        _input7 = AddInput<object>("Value 7");
        _resultOutput = AddOutput<object>("Result");
    }

    public override void Process()
    {
        try
        {
            var index = GetInputValue(_indexInput);
            var inputs = new[] { _input0, _input1, _input2, _input3, _input4, _input5, _input6, _input7 };

            if (index < 0 || index >= inputs.Length)
            {
                Error = $"Index {index} is out of range (0-{inputs.Length - 1})";
                return;
            }

            var result = GetInputValue(inputs[index]);
            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Switch error: {ex.Message}";
        }
    }
}
