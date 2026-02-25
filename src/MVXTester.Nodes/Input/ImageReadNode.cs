using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("Image Read", NodeCategories.Input, Description = "Read image from file path")]
public class ImageReadNode : BaseNode
{
    private OutputPort<Mat> _imageOutput = null!;
    private NodeProperty _filePath = null!;

    protected override void Setup()
    {
        _imageOutput = AddOutput<Mat>("Image");
        _filePath = AddFilePathProperty("FilePath", "File Path", "", "Path to the image file");
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

            var image = Cv2.ImRead(filePath, ImreadModes.Unchanged);
            if (image.Empty())
            {
                Error = $"Failed to read image: {filePath}";
                image.Dispose();
                return;
            }

            SetOutputValue(_imageOutput, image);
            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Image Read error: {ex.Message}";
        }
    }
}
