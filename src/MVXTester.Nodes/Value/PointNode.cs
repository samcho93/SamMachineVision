using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Point", NodeCategories.Value, Description = "2D Point value")]
public class PointNode : BaseNode
{
    private OutputPort<Point> _valueOutput = null!;
    private NodeProperty _x = null!;
    private NodeProperty _y = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<Point>("Point");
        _x = AddIntProperty("X", "X", 0, int.MinValue, int.MaxValue, "X coordinate");
        _y = AddIntProperty("Y", "Y", 0, int.MinValue, int.MaxValue, "Y coordinate");
    }

    public override void Process()
    {
        var point = new Point(_x.GetValue<int>(), _y.GetValue<int>());
        SetOutputValue(_valueOutput, point);
        Error = null;
    }
}
