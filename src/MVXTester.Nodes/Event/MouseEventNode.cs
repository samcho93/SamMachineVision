using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Event;

[NodeInfo("Mouse Event", NodeCategories.Event, Description = "Receive mouse events from execution output window")]
public class MouseEventNode : BaseNode, IMouseEventReceiver
{
    private OutputPort<int> _xOutput = null!;
    private OutputPort<int> _yOutput = null!;
    private OutputPort<string> _eventTypeOutput = null!;
    private OutputPort<int> _buttonOutput = null!;
    private OutputPort<bool> _isPressedOutput = null!;

    private MouseEventData? _lastEvent;
    private bool _isPressed;
    private readonly object _lock = new();

    protected override void Setup()
    {
        _xOutput = AddOutput<int>("X");
        _yOutput = AddOutput<int>("Y");
        _eventTypeOutput = AddOutput<string>("EventType");
        _buttonOutput = AddOutput<int>("Button");
        _isPressedOutput = AddOutput<bool>("IsPressed");
    }

    public void OnMouseEvent(MouseEventData eventData)
    {
        lock (_lock)
        {
            _lastEvent = eventData;

            switch (eventData.EventType)
            {
                case MouseEventType.LeftDown:
                case MouseEventType.RightDown:
                case MouseEventType.MiddleDown:
                    _isPressed = true;
                    break;
                case MouseEventType.LeftUp:
                case MouseEventType.RightUp:
                case MouseEventType.MiddleUp:
                    _isPressed = false;
                    break;
            }
        }

        IsDirty = true;
    }

    public override void Process()
    {
        lock (_lock)
        {
            if (_lastEvent != null)
            {
                SetOutputValue(_xOutput, _lastEvent.X);
                SetOutputValue(_yOutput, _lastEvent.Y);
                SetOutputValue(_eventTypeOutput, _lastEvent.EventType.ToString());
                SetOutputValue(_buttonOutput, _lastEvent.Button);
                SetOutputValue(_isPressedOutput, _isPressed);
            }
        }

        Error = null;
    }
}
