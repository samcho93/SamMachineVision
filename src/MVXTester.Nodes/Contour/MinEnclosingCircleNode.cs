using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Min Enclosing Circle", NodeCategories.Contour, Description = "Find minimum enclosing circles for contours")]
public class MinEnclosingCircleNode : BaseNode
{
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<Point2f[]> _centersOutput = null!;
    private OutputPort<float[]> _radiiOutput = null!;

    protected override void Setup()
    {
        _contoursInput = AddInput<Point[][]>("Contours");
        _centersOutput = AddOutput<Point2f[]>("Centers");
        _radiiOutput = AddOutput<float[]>("Radii");
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

            var centers = new Point2f[contours.Length];
            var radii = new float[contours.Length];

            for (int i = 0; i < contours.Length; i++)
            {
                Cv2.MinEnclosingCircle(contours[i], out var center, out var radius);
                centers[i] = center;
                radii[i] = radius;
            }

            SetOutputValue(_centersOutput, centers);
            SetOutputValue(_radiiOutput, radii);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Min Enclosing Circle error: {ex.Message}";
        }
    }
}
