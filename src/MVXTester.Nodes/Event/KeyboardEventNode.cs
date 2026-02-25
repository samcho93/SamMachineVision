using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Event;

[NodeInfo("Keyboard Event", NodeCategories.Event, Description = "Receive keyboard events from execution output window")]
public class KeyboardEventNode : BaseNode, IKeyboardEventReceiver
{
    private OutputPort<int> _keyCodeOutput = null!;
    private OutputPort<string> _keyNameOutput = null!;
    private OutputPort<bool> _isPressedOutput = null!;

    private KeyboardEventData? _lastEvent;
    private bool _isPressed;
    private readonly object _lock = new();

    protected override void Setup()
    {
        _keyCodeOutput = AddOutput<int>("KeyCode");
        _keyNameOutput = AddOutput<string>("KeyName");
        _isPressedOutput = AddOutput<bool>("IsPressed");
    }

    public void OnKeyboardEvent(KeyboardEventData eventData)
    {
        lock (_lock)
        {
            _lastEvent = eventData;
            _isPressed = eventData.EventType == KeyEventType.KeyDown;
        }

        IsDirty = true;
    }

    public override void Process()
    {
        lock (_lock)
        {
            if (_lastEvent != null)
            {
                SetOutputValue(_keyCodeOutput, _lastEvent.KeyCode);
                SetOutputValue(_keyNameOutput, _lastEvent.KeyName);
                SetOutputValue(_isPressedOutput, _isPressed);
            }
        }

        Error = null;
    }
}
