using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

public enum ImageFormat
{
    PNG,
    JPG,
    BMP,
    TIFF
}

[NodeInfo("Image Write", NodeCategories.Input, Description = "Write image to file")]
public class ImageWriteNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private NodeProperty _filePath = null!;
    private NodeProperty _format = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _filePath = AddFilePathProperty("FilePath", "File Path", "", "Output file path");
        _format = AddEnumProperty("Format", "Format", ImageFormat.PNG, "Image format");
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

            var filePath = _filePath.GetValue<string>();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Error = "File path is empty";
                return;
            }

            var format = _format.GetValue<ImageFormat>();
            var ext = format switch
            {
                ImageFormat.PNG => ".png",
                ImageFormat.JPG => ".jpg",
                ImageFormat.BMP => ".bmp",
                ImageFormat.TIFF => ".tiff",
                _ => ".png"
            };

            // Ensure correct extension
            var dir = Path.GetDirectoryName(filePath);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            var finalPath = Path.Combine(dir ?? "", nameWithoutExt + ext);

            // Ensure directory exists
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Cv2.ImWrite(finalPath, image);
            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Image Write error: {ex.Message}";
        }
    }
}
