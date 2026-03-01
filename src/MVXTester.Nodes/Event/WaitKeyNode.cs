using System.Diagnostics;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Event;

[NodeInfo("WaitKey", NodeCategories.Event, Description = "Wait for key press with timeout (like cv2.waitKey). Implements IStreamingSource to keep the runtime loop running continuously.")]
public class WaitKeyNode : BaseNode, IStreamingSource
{
    private InputPort<int> _delayInput = null!;
    private OutputPort<int> _keyCodeOutput = null!;
    private OutputPort<string> _keyNameOutput = null!;
    private OutputPort<bool> _isTimeoutOutput = null!;
    private NodeProperty _delay = null!;

    private volatile int _lastKeyCode = -1;
    private bool _subscribed;

    protected override void Setup()
    {
        _delayInput = AddInput<int>("Delay");
        _keyCodeOutput = AddOutput<int>("KeyCode");
        _keyNameOutput = AddOutput<string>("KeyName");
        _isTimeoutOutput = AddOutput<bool>("IsTimeout");
        _delay = AddIntProperty("Delay", "Delay (ms)", 1, 1, 30000, "Wait duration in milliseconds");
    }

    public override void Process()
    {
        if (!_subscribed)
        {
            RuntimeEventBus.KeyEvent += OnKeyEvent;
            _subscribed = true;
        }

        // Reset key state
        _lastKeyCode = -1;

        if (IsRuntimeMode)
        {
            var delayMs = GetPortOrProperty(_delayInput, _delay);
            if (delayMs < 1) delayMs = 1;

            // Wait for specified duration while polling for key events
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < delayMs)
            {
                if (_lastKeyCode >= 0)
                    break;
                Thread.Sleep(1);
            }
        }

        var keyCode = _lastKeyCode;
        var isTimeout = keyCode < 0;

        SetOutputValue(_keyCodeOutput, keyCode);
        SetOutputValue(_keyNameOutput, isTimeout ? "" : ((char)keyCode).ToString());
        SetOutputValue(_isTimeoutOutput, isTimeout);
        Error = null;
    }

    private void OnKeyEvent(int keyCode)
    {
        _lastKeyCode = keyCode;
        IsDirty = true;
    }

    public override void Cleanup()
    {
        if (_subscribed)
        {
            RuntimeEventBus.KeyEvent -= OnKeyEvent;
            _subscribed = false;
        }
        base.Cleanup();
    }
}
