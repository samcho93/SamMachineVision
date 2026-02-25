using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Detection;

[NodeInfo("Template Match", NodeCategories.Detection, Description = "Find template in image using template matching")]
public class TemplateMatchNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Mat> _templateInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _method = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _templateInput = AddInput<Mat>("Template");
        _resultOutput = AddOutput<Mat>("Result");
        _method = AddEnumProperty("Method", "Method", TemplateMatchModes.CCoeffNormed, "Matching method");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            var template = GetInputValue(_templateInput);

            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }
            if (template == null || template.Empty())
            {
                Error = "No template image";
                return;
            }

            var method = _method.GetValue<TemplateMatchModes>();

            var matchResult = new Mat();
            Cv2.MatchTemplate(image, template, matchResult, method);

            Cv2.MinMaxLoc(matchResult, out _, out double maxVal, out _, out Point maxLoc);
            matchResult.Dispose();

            // Draw match rectangle on result
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            var rect = new Rect(maxLoc.X, maxLoc.Y, template.Width, template.Height);
            Cv2.Rectangle(result, rect, new Scalar(0, 255, 0), 2);
            Cv2.PutText(result, $"{maxVal:F3}", new Point(maxLoc.X, maxLoc.Y - 5),
                HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Template Match error: {ex.Message}";
        }
    }
}
