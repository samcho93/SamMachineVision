using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

[NodeInfo("For Loop", NodeCategories.Control, Description = "Generate a loop index from start to end with step")]
public class ForLoopNode : BaseNode
{
    private OutputPort<int> _indexOutput = null!;
    private OutputPort<bool> _isRunningOutput = null!;
    private NodeProperty _start = null!;
    private NodeProperty _end = null!;
    private NodeProperty _step = null!;

    private int _currentIndex;
    private bool _initialized;

    protected override void Setup()
    {
        _indexOutput = AddOutput<int>("Index");
        _isRunningOutput = AddOutput<bool>("IsRunning");
        _start = AddIntProperty("Start", "Start", 0, int.MinValue, int.MaxValue, "Loop start value");
        _end = AddIntProperty("End", "End", 10, int.MinValue, int.MaxValue, "Loop end value (exclusive)");
        _step = AddIntProperty("Step", "Step", 1, 1, 10000, "Loop step increment");
    }

    public override void Process()
    {
        try
        {
            var start = _start.GetValue<int>();
            var end = _end.GetValue<int>();
            var step = _step.GetValue<int>();

            if (step <= 0) step = 1;

            if (!_initialized)
            {
                _currentIndex = start;
                _initialized = true;
            }

            if (_currentIndex < end)
            {
                SetOutputValue(_indexOutput, _currentIndex);
                SetOutputValue(_isRunningOutput, true);
                _currentIndex += step;
            }
            else
            {
                // Reset for next execution cycle
                _currentIndex = start;
                SetOutputValue(_indexOutput, _currentIndex);
                SetOutputValue(_isRunningOutput, false);
            }

            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"For Loop error: {ex.Message}";
        }
    }

    public override void Cleanup()
    {
        _initialized = false;
        base.Cleanup();
    }
}
