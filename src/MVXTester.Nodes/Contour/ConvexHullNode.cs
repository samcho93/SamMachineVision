using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Convex Hull", NodeCategories.Contour, Description = "Calculate convex hull of contours")]
public class ConvexHullNode : BaseNode
{
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<Point[][]> _hullsOutput = null!;

    protected override void Setup()
    {
        _contoursInput = AddInput<Point[][]>("Contours");
        _hullsOutput = AddOutput<Point[][]>("Hulls");
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

            var hulls = contours.Select(c => Cv2.ConvexHull(c)).ToArray();

            SetOutputValue(_hullsOutput, hulls);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Convex Hull error: {ex.Message}";
        }
    }
}
