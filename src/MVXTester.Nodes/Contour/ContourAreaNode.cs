using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Contour;

[NodeInfo("Contour Area", NodeCategories.Contour, Description = "Calculate the area of contours")]
public class ContourAreaNode : BaseNode
{
    private InputPort<Point[][]> _contoursInput = null!;
    private OutputPort<double[]> _areasOutput = null!;

    protected override void Setup()
    {
        _contoursInput = AddInput<Point[][]>("Contours");
        _areasOutput = AddOutput<double[]>("Areas");
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

            var areas = contours.Select(c => Cv2.ContourArea(c)).ToArray();

            SetOutputValue(_areasOutput, areas);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Contour Area error: {ex.Message}";
        }
    }
}
