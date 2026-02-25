using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Approx Poly", NodeCategories.Contour, Description = "Approximate contour polygons")]
public class ApproxPolyNode : BaseNode
{
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<Point[][]> _approxOutput = null!;
    private NodeProperty _epsilon = null!;

    protected override void Setup()
    {
        _contoursInput = AddInput<Point[][]>("Contours");
        _approxOutput = AddOutput<Point[][]>("Approx");
        _epsilon = AddDoubleProperty("Epsilon", "Epsilon (%)", 2.0, 0.01, 50.0, "Approximation accuracy as percentage of arc length");
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

            var epsilonPct = _epsilon.GetValue<double>();
            var approx = new Point[contours.Length][];

            for (int i = 0; i < contours.Length; i++)
            {
                var peri = Cv2.ArcLength(contours[i], true);
                var eps = peri * epsilonPct / 100.0;
                approx[i] = Cv2.ApproxPolyDP(contours[i], eps, true);
            }

            SetOutputValue(_approxOutput, approx);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Approx Poly error: {ex.Message}";
        }
    }
}
