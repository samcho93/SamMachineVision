using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Value;

[NodeInfo("Size", NodeCategories.Value, Description = "Size value (Width, Height)")]
public class SizeNode : BaseNode
{
    private OutputPort<Size> _valueOutput = null!;
    private NodeProperty _w = null!;
    private NodeProperty _h = null!;

    protected override void Setup()
    {
        _valueOutput = AddOutput<Size>("Size");
        _w = AddIntProperty("W", "Width", 100, 0, int.MaxValue, "Width");
        _h = AddIntProperty("H", "Height", 100, 0, int.MaxValue, "Height");
    }

    public override void Process()
    {
        var size = new Size(_w.GetValue<int>(), _h.GetValue<int>());
        SetOutputValue(_valueOutput, size);
        Error = null;
    }
}
