using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Bounding Rect", NodeCategories.Contour, Description = "Calculate bounding rectangles for contours")]
public class BoundingRectNode : BaseNode
{
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<Rect[]> _rectsOutput = null!;

    protected override void Setup()
    {
        _contoursInput = AddInput<Point[][]>("Contours");
        _rectsOutput = AddOutput<Rect[]>("Rects");
    }

    public override void Process()
    {
        try
        {
            var contours = GetInputValue(_contoursInput);
            if (contours == null || contours.Length == 0)
            {
                Error = "No contours input";
                return;
            }

            var rects = contours.Select(c => Cv2.BoundingRect(c)).ToArray();

            SetOutputValue(_rectsOutput, rects);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Bounding Rect error: {ex.Message}";
        }
    }
}
