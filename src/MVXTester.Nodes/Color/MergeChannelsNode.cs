using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Color;

[NodeInfo("Merge Channels", NodeCategories.Color, Description = "Merge separate channels into a single image")]
public class MergeChannelsNode : BaseNode
{
    private InputPort<Mat> _ch0Input = null!;
    private InputPort<Mat> _ch1Input = null!;
    private InputPort<Mat> _ch2Input = null!;
    private OutputPort<Mat> _resultOutput = null!;

    protected override void Setup()
    {
        _ch0Input = AddInput<Mat>("Ch0");
        _ch1Input = AddInput<Mat>("Ch1");
        _ch2Input = AddInput<Mat>("Ch2");
        _resultOutput = AddOutput<Mat>("Result");
    }

    public override void Process()
    {
        try
        {
            var ch0 = GetInputValue(_ch0Input);
            var ch1 = GetInputValue(_ch1Input);
            var ch2 = GetInputValue(_ch2Input);

            if (ch0 == null || ch0.Empty())
            {
                Error = "Channel 0 is empty";
                return;
            }

            var channels = new List<Mat> { ch0 };
            if (ch1 != null && !ch1.Empty()) channels.Add(ch1);
            if (ch2 != null && !ch2.Empty()) channels.Add(ch2);

            var result = new Mat();
            Cv2.Merge(channels.ToArray(), result);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Merge Channels error: {ex.Message}";
        }
    }
}
