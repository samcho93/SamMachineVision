using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Rect", NodeCategories.Value, Description = "Rectangle value (X, Y, Width, Height)")]
public class RectNode : BaseNode
{
    private OutputPort<Rect> _valueOutput = null!;
    private NodeProperty _x = null!;
    private NodeProperty _y = null!;
    private NodeProperty _w = null!;
    private NodeProperty _h = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<Rect>("Rect");
        _x = AddIntProperty("X", "X", 0, 0, int.MaxValue, "X position");
        _y = AddIntProperty("Y", "Y", 0, 0, int.MaxValue, "Y position");
        _w = AddIntProperty("W", "Width", 100, 0, int.MaxValue, "Width");
        _h = AddIntProperty("H", "Height", 100, 0, int.MaxValue, "Height");
    }

    public override void Process()
    {
        var rect = new Rect(
            _x.GetValue<int>(),
            _y.GetValue<int>(),
            _w.GetValue<int>(),
            _h.GetValue<int>()
        );
        SetOutputValue(_valueOutput, rect);
        Error = null;
    }
}
