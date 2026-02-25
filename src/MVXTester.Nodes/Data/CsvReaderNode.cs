using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Data;

[NodeInfo("CSV Reader", NodeCategories.Data, Description = "Read CSV file from disk")]
public class CsvReaderNode : BaseNode
{
    private OutputPort<string[][]> _dataOutput = null!;
    private OutputPort<string[]> _headersOutput = null!;
    private NodeProperty _filePath = null!;
    private NodeProperty _delimiter = null!;
    private NodeProperty _hasHeader = null!;

    protected override void Setup()
    {
        _dataOutput = AddOutput<string[][]>("Data");
        _headersOutput = AddOutput<string[]>("Headers");
        _filePath = AddFilePathProperty("FilePath", "File Path", "", "Path to CSV file");
        _delimiter = AddStringProperty("Delimiter", "Delimiter", ",", "Column delimiter");
        _hasHeader = AddBoolProperty("HasHeader", "Has Header", true, "First row is header");
    }

    public override void Process()
    {
        try
        {
            var filePath = _filePath.GetValue<string>();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Error = "File path is empty";
                return;
            }

            if (!File.Exists(filePath))
            {
                Error = $"File not found: {filePath}";
                return;
            }

            var delimiter = _delimiter.GetValue<string>();
            if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
            var hasHeader = _hasHeader.GetValue<bool>();

            var lines = File.ReadAllLines(filePath);
            if (lines.Length == 0)
            {
                Error = "CSV file is empty";
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
            Error = $"CSV Reader error: {ex.Message}";
        }
    }
}
