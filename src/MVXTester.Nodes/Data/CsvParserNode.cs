using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("CSV Parser", NodeCategories.Data, Description = "Parse CSV text string")]
public class CsvParserNode : BaseNode
{
    private InputPort<string> _csvTextInput = null!;
    private OutputPort<string[][]> _dataOutput = null!;
    private OutputPort<string[]> _headersOutput = null!;
    private NodeProperty _delimiter = null!;
    private NodeProperty _hasHeader = null!;

    protected override void Setup()
    {
        _csvTextInput = AddInput<string>("CsvText");
        _dataOutput = AddOutput<string[][]>("Data");
        _headersOutput = AddOutput<string[]>("Headers");
        _delimiter = AddStringProperty("Delimiter", "Delimiter", ",", "Column delimiter");
        _hasHeader = AddBoolProperty("HasHeader", "Has Header", true, "First row is header");
    }

    public override void Process()
    {
        try
        {
            var csvText = GetInputValue(_csvTextInput);
            if (string.IsNullOrWhiteSpace(csvText))
            {
                Error = "No CSV text input";
                return;
            }

            var delimiter = _delimiter.GetValue<string>();
            if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
            var hasHeader = _hasHeader.GetValue<bool>();

            var lines = csvText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                Error = "CSV text is empty";
                return;
            }

            var delimChar = delimiter[0];
            int startIndex = 0;
            string[] headers = Array.Empty<string>();

            if (hasHeader && lines.Length > 0)
            {
                headers = lines[0].Split(delimChar);
                startIndex = 1;
            }

            var data = new string[lines.Length - startIndex][];
            for (int i = startIndex; i < lines.Length; i++)
            {
                data[i - startIndex] = lines[i].Split(delimChar);
            }

            SetOutputValue(_dataOutput, data);
            SetOutputValue(_headersOutput, headers);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"CSV Parser error: {ex.Message}";
        }
    }
}
