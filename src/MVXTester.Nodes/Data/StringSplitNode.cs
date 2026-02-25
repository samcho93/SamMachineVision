using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("String Split", NodeCategories.Data, Description = "Split string by delimiter")]
public class StringSplitNode : BaseNode
{
    private InputPort<string> _textInput = null!;
    private OutputPort<string[]> _partsOutput = null!;
    private NodeProperty _delimiter = null!;

    protected override void Setup()
    {
        _textInput = AddInput<string>("Text");
        _partsOutput = AddOutput<string[]>("Parts");
        _delimiter = AddStringProperty("Delimiter", "Delimiter", ",", "Split delimiter");
    }

    public override void Process()
    {
        try
        {
            var text = GetInputValue(_textInput);
            if (text == null)
            {
                SetOutputValue(_partsOutput, Array.Empty<string>());
                Error = null;
                return;
            }

            var delimiter = _delimiter.GetValue<string>();
            if (string.IsNullOrEmpty(delimiter)) delimiter = ",";

            var parts = text.Split(new[] { delimiter }, StringSplitOptions.None);
            SetOutputValue(_partsOutput, parts);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"String Split error: {ex.Message}";
        }
    }
}
