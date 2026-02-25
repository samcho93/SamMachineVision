using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("String Replace", NodeCategories.Data, Description = "Find and replace in string")]
public class StringReplaceNode : BaseNode
{
    private InputPort<string> _textInput = null!;
    private OutputPort<string> _resultOutput = null!;
    private NodeProperty _find = null!;
    private NodeProperty _replace = null!;

    protected override void Setup()
    {
        _textInput = AddInput<string>("Text");
        _resultOutput = AddOutput<string>("Result");
        _find = AddStringProperty("Find", "Find", "", "Text to find");
        _replace = AddStringProperty("Replace", "Replace", "", "Replacement text");
    }

    public override void Process()
    {
        try
        {
            var text = GetInputValue(_textInput);
            if (text == null)
            {
                SetOutputValue(_resultOutput, "");
                Error = null;
                return;
            }

            var find = _find.GetValue<string>();
            var replace = _replace.GetValue<string>();

            if (string.IsNullOrEmpty(find))
            {
                SetOutputValue(_resultOutput, text);
                Error = null;
                return;
            }

            var result = text.Replace(find, replace ?? "");
            SetOutputValue(_resultOutput, result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"String Replace error: {ex.Message}";
        }
    }
}
