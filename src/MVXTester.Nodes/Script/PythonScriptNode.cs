using System.Diagnostics;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Script;

[NodeInfo("Python Script", NodeCategories.Script, Description = "Execute Python script using system Python")]
public class PythonScriptNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _script = null!;
    private NodeProperty _pythonPath = null!;
    private NodeProperty _timeout = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _script = AddMultilineStringProperty("Script", "Python Script",
            "import cv2\nimport numpy as np\nimport sys\n\n# Read input image\nimg = cv2.imread(sys.argv[1])\n\n# Process image\nresult = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)\n\n# Write output image\ncv2.imwrite(sys.argv[2], result)",
            "Python script to execute. Use sys.argv[1] for input and sys.argv[2] for output image paths.");
        _pythonPath = AddStringProperty("PythonPath", "Python Path", "python", "Path to Python executable");
        _timeout = AddIntProperty("Timeout", "Timeout (ms)", 30000, 1000, 300000, "Script execution timeout");
    }

    public override void Process()
    {
        try
        {
            var script = _script.GetValue<string>();
            if (string.IsNullOrWhiteSpace(script))
            {
                Error = "Script is empty";
                return;
            }

            var pythonPath = _pythonPath.GetValue<string>();
            var timeout = _timeout.GetValue<int>();

            // Create temp files
            var tempDir = Path.Combine(Path.GetTempPath(), "MVXTester_Python");
            Directory.CreateDirectory(tempDir);

            var scriptFile = Path.Combine(tempDir, $"script_{Id}.py");
            var inputFile = Path.Combine(tempDir, $"input_{Id}.png");
            var outputFile = Path.Combine(tempDir, $"output_{Id}.png");

            // Save script
            File.WriteAllText(scriptFile, script);

            // Save input image if available
            var inputImage = GetInputValue(_imageInput);
            if (inputImage != null && !inputImage.Empty())
            {
                Cv2.ImWrite(inputFile, inputImage);
            }

            // Execute Python script
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{scriptFile}\" \"{inputFile}\" \"{outputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                Error = "Failed to start Python process";
                return;
            }

            try
            {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(timeout))
            {
                process.Kill();
                process.Dispose();
                Error = "Python script timed out";
                return;
            }

            if (process.ExitCode != 0)
            {
                Error = $"Python script failed (exit code {process.ExitCode}): {stderr}";
                return;
            }

            // Read output image if it exists
            if (File.Exists(outputFile))
            {
                var result = Cv2.ImRead(outputFile, ImreadModes.Unchanged);
                if (!result.Empty())
                {
                    SetOutputValue(_resultOutput, result);
                    SetPreview(result);
                }
                else
                {
                    result.Dispose();
                }

                // Clean up output file
                try { File.Delete(outputFile); } catch { }
            }

            // Clean up temp files
            try { File.Delete(scriptFile); } catch { }
            try { File.Delete(inputFile); } catch { }

            Error = null;
            }
            finally
            {
                process.Dispose();
            }
        }
        catch (Exception ex)
        {
            Error = $"Python Script error: {ex.Message}";
        }
    }
}
