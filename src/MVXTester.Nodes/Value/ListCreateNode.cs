using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("List Create", NodeCategories.Value, Description = "Create a list from multiple inputs")]
public class ListCreateNode : BaseNode
{
    private InputPort<object> _input0 = null!;
    private InputPort<object> _input1 = null!;
    private InputPort<object> _input2 = null!;
    private InputPort<object> _input3 = null!;
    private InputPort<object> _input4 = null!;
    private InputPort<object> _input5 = null!;
    private InputPort<object> _input6 = null!;
    private InputPort<object> _input7 = null!;
    private OutputPort<List<object>> _listOutput = null!;

    protected override void Setup()
    {
        _input0 = AddInput<object>("Item 0");
        _input1 = AddInput<object>("Item 1");
        _input2 = AddInput<object>("Item 2");
        _input3 = AddInput<object>("Item 3");
        _input4 = AddInput<object>("Item 4");
        _input5 = AddInput<object>("Item 5");
        _input6 = AddInput<object>("Item 6");
        _input7 = AddInput<object>("Item 7");
        _listOutput = AddOutput<List<object>>("List");
    }

    public override void Process()
    {
        try
        {
            var list = new List<object>();
            var inputs = new[] { _input0, _input1, _input2, _input3, _input4, _input5, _input6, _input7 };

            foreach (var input in inputs)
            {
                var value = GetInputValue(input);
                if (value != null)
                    list.Add(value);
            }

            SetOutputValue(_listOutput, list);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"List Create error: {ex.Message}";
        }
    }
}
