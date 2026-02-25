using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("String Format", NodeCategories.Value, Description = "Format string with placeholders {0}, {1}, etc.")]
public class StringFormatNode : BaseNode
{
    private InputPort<object> _arg0Input = null!;
    private InputPort<object> _arg1Input = null!;
    private InputPort<object> _arg2Input = null!;
    private InputPort<object> _arg3Input = null!;
    private OutputPort<string> _resultOutput = null!;
    private NodeProperty _format = null!;

    protected override void Setup()
    {
        _arg0Input = AddInput<object>("Arg 0");
        _arg1Input = AddInput<object>("Arg 1");
        _arg2Input = AddInput<object>("Arg 2");
        _arg3Input = AddInput<object>("Arg 3");
        _resultOutput = AddOutput<string>("Result");
        _format = AddStringProperty("Format", "Format", "{0}", "Format template using {0}, {1}, etc.");
    }

    public override void Process()
    {
        try
        {
            var format = _format.GetValue<string>();
            var args = new object?[]
            {
                GetInputValue(_arg0Input),
                GetInputValue(_arg1Input),
                GetInputValue(_arg2Input),
                GetInputValue(_arg3Input)
            };

            // Replace null with empty string
            var safeArgs = args.Select(a => a ?? (object)"").ToArray();
            var result = string.Format(format, safeArgs);

            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"String Format error: {ex.Message}";
        }
    }
}
