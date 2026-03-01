using System.Diagnostics;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Control;

[NodeInfo("Delay", NodeCategories.Control, Description = "Delay execution for specified milliseconds (only active in runtime mode)")]
public class DelayNode : BaseNode
{
    private InputPort<int> _msInput = null!;
    private OutputPort<double> _elapsedOutput = null!;
    private NodeProperty _milliseconds = null!;

    protected override void Setup()
    {
        _msInput = AddInput<int>("Milliseconds");
        _elapsedOutput = AddOutput<double>("Elapsed");
        _milliseconds = AddIntProperty("Milliseconds", "Delay (ms)", 100, 1, 60000, "Delay duration in milliseconds");
    }

    public override void Process()
    {
        var delayMs = GetPortOrProperty(_msInput, _milliseconds);
        if (delayMs < 1) delayMs = 1;

        if (IsRuntimeMode)
        {
            var sw = Stopwatch.StartNew();
            Thread.Sleep(delayMs);
            sw.Stop();
            SetOutputValue(_elapsedOutput, sw.Elapsed.TotalMilliseconds);
        }
        else
        {
            SetOutputValue(_elapsedOutput, 0.0);
        }

        Error = null;
    }
}
