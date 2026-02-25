using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using System.Globalization;

namespace MVXTester.Nodes.Filter;

[NodeInfo("Filter 2D", NodeCategories.Filter, Description = "Apply custom convolution kernel")]
public class Filter2DNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _kernel = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _kernel = AddMultilineStringProperty("Kernel", "Kernel", "0 -1 0\n-1 5 -1\n0 -1 0",
            "Kernel matrix (space-separated values, one row per line)");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var kernelStr = _kernel.GetValue<string>();
            if (string.IsNullOrWhiteSpace(kernelStr))
            {
                Error = "Kernel is empty";
                return;
            }

            // Parse kernel from multiline string
            var lines = kernelStr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var rows = new List<double[]>();
            foreach (var line in lines)
            {
                var values = line.Trim().Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                var row = values.Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture)).ToArray();
                rows.Add(row);
            }

            if (rows.Count == 0)
            {
                Error = "Invalid kernel";
                return;
            }

            int kernelRows = rows.Count;
            int kernelCols = rows[0].Length;

            var kernelData = new float[kernelRows, kernelCols];
            for (int r = 0; r < kernelRows; r++)
            {
                if (rows[r].Length != kernelCols)
                {
                    Error = "Kernel rows have different lengths";
                    return;
                }
                for (int c = 0; c < kernelCols; c++)
                {
                    kernelData[r, c] = (float)rows[r][c];
                }
            }

            var kernel = Mat.FromArray(kernelData);
            var result = new Mat();
            Cv2.Filter2D(image, result, -1, kernel);

            kernel.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Filter2D error: {ex.Message}";
        }
    }
}
