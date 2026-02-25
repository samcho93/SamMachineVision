using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Event;

[NodeInfo("Mouse ROI", NodeCategories.Event, Description = "Draw ROI rectangle with mouse on execution output window")]
public class MouseRoiNode : BaseNode, IMouseEventReceiver
{
    private OutputPort<Rect> _rectOutput = null!;
    private OutputPort<bool> _isDrawingOutput = null!;

    private Point _startPoint;
    private Point _endPoint;
    private bool _isDrawing;
    private bool _hasRect;
    private readonly object _lock = new();

    protected override void Setup()
    {
        _rectOutput = AddOutput<Rect>("Rect");
        _isDrawingOutput = AddOutput<bool>("IsDrawing");
    }

    public void OnMouseEvent(MouseEventData eventData)
    {
        lock (_lock)
        {
            switch (eventData.EventType)
            {
                case MouseEventType.LeftDown:
                    _startPoint = new Point(eventData.X, eventData.Y);
                    _endPoint = _startPoint;
                    _isDrawing = true;
                    _hasRect = false;
                    break;

                case MouseEventType.Move:
                    if (_isDrawing)
                    {
                        _endPoint = new Point(eventData.X, eventData.Y);
                    }
                    break;

                case MouseEventType.LeftUp:
                    if (_isDrawing)
                    {
                        _endPoint = new Point(eventData.X, eventData.Y);
                        _isDrawing = false;
                        _hasRect = true;
                    }
                    break;
            }
        }

        IsDirty = true;
    }

    public override void Process()
    {
        lock (_lock)
        {
            SetOutputValue(_isDrawingOutput, _isDrawing);

            if (_hasRect || _isDrawing)
            {
                var x = Math.Min(_startPoint.X, _endPoint.X);
                var y = Math.Min(_startPoint.Y, _endPoint.Y);
                var w = Math.Abs(_endPoint.X - _startPoint.X);
                var h = Math.Abs(_endPoint.Y - _startPoint.Y);

                if (w > 0 && h > 0)
                {
                    SetOutputValue(_rectOutput, new Rect(x, y, w, h));
                }
            }
        }

        Error = null;
    }
}
