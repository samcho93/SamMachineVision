using System.Diagnostics;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Script;

[NodeInfo("C# Script", NodeCategories.Script, Description = "Execute C# script with OpenCvSharp (requires .NET SDK)")]
public class CSharpScriptNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private NodeProperty _script = null!;
    private NodeProperty _timeout = null!;

    private string? _projectDir;

    private const string DefaultScript =
        "using OpenCvSharp;\n\n" +
        "// Input image path: args[0]\n" +
        "// Output image path: args[1]\n\n" +
        "var input = Cv2.ImRead(args[0]);\n\n" +
        "// --- Your processing code ---\n" +
        "var result = new Mat();\n" +
        "Cv2.CvtColor(input, result, ColorConversionCodes.BGR2GRAY);\n" +
        "// --- End ---\n\n" +
        "Cv2.ImWrite(args[1], result);\n" +
        "input.Dispose();\n" +
        "result.Dispose();\n";

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _resultOutput = AddOutput<Mat>("Result");
        _script = AddMultilineStringProperty("Script", "C# Script", DefaultScript,
            "C# script to execute. Use args[0] for input and args[1] for output image paths. OpenCvSharp is available.");
        _timeout = AddIntProperty("Timeout", "Timeout (ms)", 60000, 5000, 600000,
            "Script execution timeout (first run is slow due to build)");
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

            var timeout = _timeout.GetValue<int>();

            // Ensure project directory exists
            EnsureProjectDir();

            var inputFile = Path.Combine(_projectDir!, $"input_{Id}.png");
            var outputFile = Path.Combine(_projectDir!, $"output_{Id}.png");

            // Write Program.cs
            var programCs = Path.Combine(_projectDir!, "Program.cs");
            File.WriteAllText(programCs, script);

            // Save input image if available
            var inputImage = GetInputValue(_imageInput);
            if (inputImage != null && !inputImage.Empty())
            {
                Cv2.ImWrite(inputFile, inputImage);
            }

            // Execute: dotnet run
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{_projectDir}\" -- \"{inputFile}\" \"{outputFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = _projectDir
            };

            var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                Error = "Failed to start dotnet process";
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
                    Error = "C# script timed out";
                    return;
                }

                if (process.ExitCode != 0)
                {
                    // Trim verbose build output, show only the error
                    var errorMsg = stderr;
                    var errorIdx = stderr.IndexOf("error ", StringComparison.OrdinalIgnoreCase);
                    if (errorIdx >= 0)
                        errorMsg = stderr[errorIdx..];
                    if (errorMsg.Length > 500)
                        errorMsg = errorMsg[..500] + "...";

                    Error = $"C# script failed (exit {process.ExitCode}): {errorMsg}";
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

                    try { File.Delete(outputFile); } catch { }
                }

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
            Error = $"C# Script error: {ex.Message}";
        }
    }

    private void EnsureProjectDir()
    {
        if (_projectDir != null && Directory.Exists(_projectDir))
        {
            // Project already exists, check csproj is intact
            var csproj = Path.Combine(_projectDir, "CSharpScript.csproj");
            if (File.Exists(csproj)) return;
        }

        _projectDir = Path.Combine(Path.GetTempPath(), "MVXTester_CSharp", $"script_{Id}");
        Directory.CreateDirectory(_projectDir);

        // Detect the OpenCvSharp version from currently loaded assembly
        var ocvVersion = typeof(Cv2).Assembly.GetName().Version;
        var versionStr = ocvVersion != null ? $"{ocvVersion.Major}.{ocvVersion.Minor}.*" : "4.*";

        // Detect runtime package
        var runtimePkg = DetectRuntimePackage();

        var csprojContent =
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="OpenCvSharp4" Version="{versionStr}" />
                <PackageReference Include="{runtimePkg}" Version="{versionStr}" />
              </ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(_projectDir, "CSharpScript.csproj"), csprojContent);
    }

    private static string DetectRuntimePackage()
    {
        // Check if OpenCvSharp4.runtime.win is loaded
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name ?? "";
            if (name.Contains("OpenCvSharpExtern", StringComparison.OrdinalIgnoreCase))
            {
                var loc = asm.Location;
                if (loc.Contains("linux", StringComparison.OrdinalIgnoreCase))
                    return "OpenCvSharp4.runtime.linux-x64";
                if (loc.Contains("osx", StringComparison.OrdinalIgnoreCase))
                    return "OpenCvSharp4.runtime.osx-x64";
            }
        }
        return "OpenCvSharp4.runtime.win";
    }
}
