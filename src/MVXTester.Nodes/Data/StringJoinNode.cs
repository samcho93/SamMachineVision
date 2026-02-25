using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("String Join", NodeCategories.Data, Description = "Join string array with delimiter")]
public class StringJoinNode : BaseNode
{
    private InputPort<string[]> _partsInput = null!;
    private OutputPort<string> _textOutput = null!;
    private NodeProperty _delimiter = null!;

    protected override void Setup()
    {
        _partsInput = AddInput<string[]>("Parts");
        _textOutput = AddOutput<string>("Text");
        _delimiter = AddStringProperty("Delimiter", "Delimiter", ",", "Join delimiter");
    }

    public override void Process()
    {
        try
        {
            var parts = GetInputValue(_partsInput);
            if (parts == null || parts.Length == 0)
            {
                SetOutputValue(_textOutput, "");
                Error = null;
                return;
            }

            var delimiter = _delimiter.GetValue<string>();
            var result = string.Join(delimiter ?? "", parts);

            SetOutputValue(_textOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"String Join error: {ex.Message}";
        }
    }
}
