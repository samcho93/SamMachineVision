using System.Globalization;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("String to Number", NodeCategories.Data, Description = "Convert string to number")]
public class StringToNumberNode : BaseNode
{
    private InputPort<string> _textInput = null!;
    private OutputPort<double> _valueOutput = null!;

    protected override void Setup()
    {
        _textInput = AddInput<string>("Text");
        _valueOutput = AddOutput<double>("Value");
    }

    public override void Process()
    {
        try
        {
            var text = GetInputValue(_textInput);
            if (string.IsNullOrWhiteSpace(text))
            {
                SetOutputValue(_valueOutput, 0.0);
                Error = null;
                return;
            }

            if (double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                SetOutputValue(_valueOutput, value);
                Error = null;
            }
            else
            {
                Error = $"Cannot parse '{text}' as a number";
            }
        }
        catch (Exception ex)
        {
            Error = $"String to Number error: {ex.Message}";
        }
    }
}
