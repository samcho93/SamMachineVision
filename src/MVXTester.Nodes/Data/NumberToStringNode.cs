using System.Globalization;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("Number to String", NodeCategories.Data, Description = "Convert number to formatted string")]
public class NumberToStringNode : BaseNode
{
    private InputPort<double> _valueInput = null!;
    private OutputPort<string> _textOutput = null!;
    private NodeProperty _format = null!;

    protected override void Setup()
    {
        _valueInput = AddInput<double>("Value");
        _textOutput = AddOutput<string>("Text");
        _format = AddStringProperty("Format", "Format", "F2", "Number format string (e.g., F2, N0, E3)");
    }

    public override void Process()
    {
        try
        {
            var value = GetInputValue(_valueInput);
            var format = _format.GetValue<string>();

            string result;
            if (string.IsNullOrWhiteSpace(format))
                result = value.ToString(CultureInfo.InvariantCulture);
            else
                result = value.ToString(format, CultureInfo.InvariantCulture);

            SetOutputValue(_textOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Number to String error: {ex.Message}";
        }
    }
}
