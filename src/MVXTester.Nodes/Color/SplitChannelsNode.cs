using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Color;

[NodeInfo("Split Channels", NodeCategories.Color, Description = "Split image into separate channels")]
public class SplitChannelsNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _ch0Output = null!;
    private OutputPort<Mat> _ch1Output = null!;
    private OutputPort<Mat> _ch2Output = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _ch0Output = AddOutput<Mat>("Ch0");
        _ch1Output = AddOutput<Mat>("Ch1");
        _ch2Output = AddOutput<Mat>("Ch2");
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

            Mat[] channels;
            Cv2.Split(image, out channels);

            if (channels.Length >= 1) SetOutputValue(_ch0Output, channels[0]);
            if (channels.Length >= 2) SetOutputValue(_ch1Output, channels[1]);
            if (channels.Length >= 3) SetOutputValue(_ch2Output, channels[2]);

            if (channels.Length >= 1) SetPreview(channels[0]);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Split Channels error: {ex.Message}";
        }
    }
}
