using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

[NodeInfo("Image Show", NodeCategories.Input, Description = "Display image in OpenCV window")]
public class ImageShowNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private NodeProperty _windowName = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _windowName = AddStringProperty("WindowName", "Window Name", "Image", "Display window name");
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

            var windowName = _windowName.GetValue<string>();
            if (string.IsNullOrWhiteSpace(windowName))
                windowName = "Image";

            Cv2.ImShow(windowName, image);
            Cv2.WaitKey(1);

            SetPreview(image);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Image Show error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        try
        {
            var windowName = _windowName.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(windowName))
                Cv2.DestroyWindow(windowName);
        }
        catch { }
        base.Cleanup();
    }
}
