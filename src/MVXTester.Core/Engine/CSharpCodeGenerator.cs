using System.Text;
using MVXTester.Core.Models;

namespace MVXTester.Core.Engine;

/// <summary>
/// Generates executable C# code using OpenCvSharp from a node graph.
/// </summary>
public static class CSharpCodeGenerator
{
    /// <summary>
    /// Generates a complete C# program from the given node graph.
    /// Performs topological sort, maps each node to its OpenCvSharp equivalent,
    /// and wires inputs/outputs via named variables with proper disposal.
    /// </summary>
    public static string Generate(NodeGraph graph)
    {
        var sb = new StringBuilder();
        var sorted = GraphExecutor.TopologicalSort(graph.Nodes, graph.Connections);

        // Build a lookup: node -> variable name for each output port
        var outputVars = new Dictionary<(string NodeId, string PortName), string>();

        // Track which variables are Mat types for disposal
        var matVars = new HashSet<string>();

        // Scan for special usings
        bool needsSockets = false;
        bool needsSerial = false;
        bool needsCsv = false;
        bool needsOnnx = false;
        bool needsHttp = false;
        bool needsTesseract = false;

        foreach (var node in sorted)
        {
            var name = node.Name;
            if (name.Contains("TCP") || name.Contains("Socket"))
                needsSockets = true;
            if (name.Contains("Serial"))
                needsSerial = true;
            if (name.Contains("CSV"))
                needsCsv = true;
            if (name.Contains("MP ") || node.Category == "MediaPipe" || name.Contains("YOLO") || node.Category == "YOLO" || name.Contains("PaddleOCR"))
                needsOnnx = true;
            if (name.Contains("OpenAI") || name.Contains("Gemini") || name.Contains("Claude"))
                needsHttp = true;
            if (name.Contains("Tesseract"))
                needsTesseract = true;
        }

        // --- NuGet package hints ---
        var nugetPackages = new List<string> { "OpenCvSharp4", "OpenCvSharp4.runtime.win" };
        if (needsOnnx) nugetPackages.Add("Microsoft.ML.OnnxRuntime");
        if (needsTesseract) nugetPackages.Add("Tesseract");
        if (needsSerial) nugetPackages.Add("System.IO.Ports");

        // --- Using statements ---
        sb.AppendLine($"// NuGet: {string.Join(", ", nugetPackages)}");
        sb.AppendLine($"// dotnet add package {string.Join(" && dotnet add package ", nugetPackages)}");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using OpenCvSharp;");
        if (needsSockets)
        {
            sb.AppendLine("using System.Net;");
            sb.AppendLine("using System.Net.Sockets;");
            sb.AppendLine("using System.Text;");
        }
        if (needsSerial)
            sb.AppendLine("using System.IO.Ports;");
        if (needsOnnx)
            sb.AppendLine("using Microsoft.ML.OnnxRuntime;");
        if (needsHttp)
        {
            sb.AppendLine("using System.Net.Http;");
            sb.AppendLine("using System.Net.Http.Headers;");
            sb.AppendLine("using System.Text.Json;");
        }
        if (needsTesseract)
            sb.AppendLine("using Tesseract;");
        sb.AppendLine();

        sb.AppendLine("namespace GeneratedPipeline;");
        sb.AppendLine();
        sb.AppendLine("public class Program");
        sb.AppendLine("{");
        sb.AppendLine("    public static void Main(string[] args)");
        sb.AppendLine("    {");

        if (sorted.Count == 0)
        {
            sb.AppendLine("        // Empty graph - nothing to execute");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- Generate code for each node ---
        foreach (var node in sorted)
        {
            sb.AppendLine();
            sb.AppendLine($"        // --- {node.Name} (id: {node.Id}) ---");

            // Resolve input variable references
            var inputVarMap = new Dictionary<string, string>();
            foreach (var inp in node.Inputs)
            {
                if (inp.Connection != null)
                {
                    var srcNode = inp.Connection.Source.Owner;
                    var srcPort = inp.Connection.Source.Name;
                    var key = (srcNode.Id, srcPort);
                    if (outputVars.TryGetValue(key, out var varName))
                    {
                        inputVarMap[inp.Name] = varName;
                    }
                }
            }

            // Generate output variable names for this node
            var resultVars = new List<string>();
            foreach (var outp in node.Outputs)
            {
                var varName = $"node_{node.Id}_{SanitizeName(outp.Name)}";
                outputVars[(node.Id, outp.Name)] = varName;
                resultVars.Add(varName);
            }

            var primaryResult = resultVars.Count > 0 ? resultVars[0] : $"node_{node.Id}_result";

            // Build property values dict
            var props = new Dictionary<string, object?>();
            foreach (var p in node.Properties)
            {
                props[p.Name] = p.Value;
            }

            // Get first and second input variable names (common pattern)
            string input1 = GetInput(inputVarMap, node, 0);
            string input2 = GetInput(inputVarMap, node, 1);

            // Map node by name to C# code
            GenerateNodeCode(sb, node, primaryResult, resultVars, inputVarMap, props, input1, input2, matVars);
        }

        // --- Dispose Mat variables ---
        sb.AppendLine();
        sb.AppendLine("        // --- Cleanup ---");
        sb.AppendLine("        Cv2.DestroyAllWindows();");
        if (matVars.Count > 0)
        {
            foreach (var matVar in matVars)
            {
                sb.AppendLine($"        {matVar}?.Dispose();");
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateNodeCode(
        StringBuilder sb,
        INode node,
        string primaryResult,
        List<string> resultVars,
        Dictionary<string, string> inputVarMap,
        Dictionary<string, object?> props,
        string input1,
        string input2,
        HashSet<string> matVars)
    {
        var name = node.Name;

        // =====================================================================
        // INPUT / OUTPUT
        // =====================================================================
        if (MatchName(name, "Read Image", "Load Image", "imread"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"        using var {primaryResult} = Cv2.ImRead({CStr(path)});");
        }
        else if (MatchName(name, "Write Image", "Save Image", "imwrite"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"        Cv2.ImWrite({CStr(path)}, {input1});");
        }
        else if (MatchName(name, "Video Capture", "Camera", "VideoCapture"))
        {
            var src = GetPropStr(props, "Source", "DeviceIndex", "Path");
            var sourceVal = int.TryParse(src, out var idx) ? idx.ToString() : CStr(src);
            sb.AppendLine($"        using var {primaryResult} = new VideoCapture({sourceVal});");
        }
        else if (MatchName(name, "Read Frame", "Video Read"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        {input1}.Read({primaryResult});");
        }
        else if (MatchName(name, "Display", "Show Image", "imshow", "Image Display"))
        {
            var winName = GetPropStr(props, "WindowName", "Title");
            if (string.IsNullOrEmpty(winName)) winName = $"Display_{node.Id}";
            sb.AppendLine($"        Cv2.ImShow({CStr(winName)}, {input1});");
            sb.AppendLine($"        Cv2.WaitKey(1);");
        }
        else if (MatchName(name, "Video Writer", "VideoWriter"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            var fourcc = GetPropStr(props, "FourCC", "Codec");
            if (string.IsNullOrEmpty(fourcc)) fourcc = "XVID";
            var fps = GetPropNum(props, "FPS", "FrameRate", 30);
            sb.AppendLine($"        var fourcc_{node.Id} = VideoWriter.FourCC('{fourcc[0]}', '{(fourcc.Length > 1 ? fourcc[1] : 'X')}', '{(fourcc.Length > 2 ? fourcc[2] : 'V')}', '{(fourcc.Length > 3 ? fourcc[3] : 'I')}');");
            sb.AppendLine($"        using var {primaryResult} = new VideoWriter({CStr(path)}, fourcc_{node.Id}, {fps}, new Size(640, 480));");
        }
        else if (MatchName(name, "Wait Key", "WaitKey"))
        {
            var delay = GetPropNum(props, "Delay", "Milliseconds", 0);
            sb.AppendLine($"        var {primaryResult} = Cv2.WaitKey({delay});");
        }

        // =====================================================================
        // COLOR
        // =====================================================================
        else if (MatchName(name, "Convert Color", "CvtColor", "Color Convert", "Color Space"))
        {
            var code = GetPropStr(props, "ConversionCode", "Code", "ColorConversion");
            if (string.IsNullOrEmpty(code)) code = "ColorConversionCodes.BGR2GRAY";
            else if (!code.Contains(".")) code = $"ColorConversionCodes.{code}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.CvtColor({input1}, {primaryResult}, {code});");
        }
        else if (MatchName(name, "In Range", "InRange", "Color Range"))
        {
            var lower = GetPropStr(props, "Lower", "LowerBound", "LowerB");
            var upper = GetPropStr(props, "Upper", "UpperBound", "UpperB");
            if (string.IsNullOrEmpty(lower)) lower = "new Scalar(0, 0, 0)";
            if (string.IsNullOrEmpty(upper)) upper = "new Scalar(255, 255, 255)";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.InRange({input1}, {lower}, {upper}, {primaryResult});");
        }
        else if (MatchName(name, "Split Channels", "Split", "Channel Split"))
        {
            sb.AppendLine($"        Mat[] {primaryResult}_arr = Cv2.Split({input1});");
            if (resultVars.Count >= 3)
            {
                sb.AppendLine($"        var {resultVars[0]} = {primaryResult}_arr[0];");
                sb.AppendLine($"        var {resultVars[1]} = {primaryResult}_arr[1];");
                sb.AppendLine($"        var {resultVars[2]} = {primaryResult}_arr[2];");
                matVars.Add(resultVars[0]);
                matVars.Add(resultVars[1]);
                matVars.Add(resultVars[2]);
            }
            else
            {
                sb.AppendLine($"        var {primaryResult} = {primaryResult}_arr;");
            }
        }
        else if (MatchName(name, "Merge Channels", "Merge", "Channel Merge"))
        {
            var channels = string.Join(", ", node.Inputs
                .Where(i => inputVarMap.ContainsKey(i.Name))
                .Select(i => inputVarMap[i.Name]));
            if (string.IsNullOrEmpty(channels)) channels = input1;
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Merge(new[] {{ {channels} }}, {primaryResult});");
        }

        // =====================================================================
        // FILTER
        // =====================================================================
        else if (MatchName(name, "Gaussian Blur", "GaussianBlur"))
        {
            var kx = GetPropNum(props, "KernelWidth", "KernelX", "KernelSize", 5);
            var ky = GetPropNum(props, "KernelHeight", "KernelY", "KernelSize", 5);
            var sigmaX = GetPropDouble(props, "SigmaX", "Sigma", 0);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.GaussianBlur({input1}, {primaryResult}, new Size({kx}, {ky}), {sigmaX});");
        }
        else if (MatchName(name, "Median Blur", "MedianBlur"))
        {
            var k = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.MedianBlur({input1}, {primaryResult}, {k});");
        }
        else if (MatchName(name, "Bilateral Filter", "BilateralFilter"))
        {
            var d = GetPropNum(props, "Diameter", "D", 9);
            var sigmaColor = GetPropDouble(props, "SigmaColor", 75);
            var sigmaSpace = GetPropDouble(props, "SigmaSpace", 75);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.BilateralFilter({input1}, {primaryResult}, {d}, {sigmaColor}, {sigmaSpace});");
        }
        else if (MatchName(name, "Blur", "Average Blur", "Box Blur", "BoxFilter"))
        {
            var kx = GetPropNum(props, "KernelWidth", "KernelX", "KernelSize", 5);
            var ky = GetPropNum(props, "KernelHeight", "KernelY", "KernelSize", 5);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Blur({input1}, {primaryResult}, new Size({kx}, {ky}));");
        }
        else if (MatchName(name, "Filter2D", "Custom Filter", "Convolution"))
        {
            sb.AppendLine($"        // Define your custom kernel");
            sb.AppendLine($"        var kernel_{node.Id} = new Mat(3, 3, MatType.CV_32F, new float[] {{ 0, -1, 0, -1, 5, -1, 0, -1, 0 }});");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Filter2D({input1}, {primaryResult}, -1, kernel_{node.Id});");
            sb.AppendLine($"        kernel_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "Sharpen"))
        {
            var amount = GetPropDouble(props, "Amount", "Strength", 1.5);
            sb.AppendLine($"        var blurred_{node.Id} = new Mat();");
            sb.AppendLine($"        Cv2.GaussianBlur({input1}, blurred_{node.Id}, new Size(0, 0), 3);");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.AddWeighted({input1}, {amount}, blurred_{node.Id}, {1.0 - amount}, 0, {primaryResult});");
            sb.AppendLine($"        blurred_{node.Id}.Dispose();");
        }

        // =====================================================================
        // EDGE DETECTION
        // =====================================================================
        else if (MatchName(name, "Canny", "Canny Edge"))
        {
            var t1 = GetPropDouble(props, "Threshold1", "LowerThreshold", "MinThreshold", 100);
            var t2 = GetPropDouble(props, "Threshold2", "UpperThreshold", "MaxThreshold", 200);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Canny({input1}, {primaryResult}, {t1}, {t2});");
        }
        else if (MatchName(name, "Sobel"))
        {
            var dx = GetPropNum(props, "DX", "Dx", "OrderX", 1);
            var dy = GetPropNum(props, "DY", "Dy", "OrderY", 0);
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 3);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Sobel({input1}, {primaryResult}, MatType.CV_64F, {dx}, {dy}, ksize: {ksize});");
        }
        else if (MatchName(name, "Laplacian"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 3);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Laplacian({input1}, {primaryResult}, MatType.CV_64F, ksize: {ksize});");
        }
        else if (MatchName(name, "Scharr"))
        {
            var dx = GetPropNum(props, "DX", "Dx", "OrderX", 1);
            var dy = GetPropNum(props, "DY", "Dy", "OrderY", 0);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Scharr({input1}, {primaryResult}, MatType.CV_64F, {dx}, {dy});");
        }

        // =====================================================================
        // MORPHOLOGY
        // =====================================================================
        else if (MatchName(name, "Erode"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            var iterations = GetPropNum(props, "Iterations", 1);
            sb.AppendLine($"        var kernel_{node.Id} = Cv2.GetStructuringElement(MorphShapes.Rect, new Size({ksize}, {ksize}));");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Erode({input1}, {primaryResult}, kernel_{node.Id}, iterations: {iterations});");
            sb.AppendLine($"        kernel_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "Dilate"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            var iterations = GetPropNum(props, "Iterations", 1);
            sb.AppendLine($"        var kernel_{node.Id} = Cv2.GetStructuringElement(MorphShapes.Rect, new Size({ksize}, {ksize}));");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Dilate({input1}, {primaryResult}, kernel_{node.Id}, iterations: {iterations});");
            sb.AppendLine($"        kernel_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "Morphology", "Morphology Ex", "MorphologyEx"))
        {
            var op = GetPropStr(props, "Operation", "MorphOp", "Op");
            if (string.IsNullOrEmpty(op)) op = "MorphTypes.Open";
            else if (!op.Contains(".")) op = $"MorphTypes.{op}";
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"        var kernel_{node.Id} = Cv2.GetStructuringElement(MorphShapes.Rect, new Size({ksize}, {ksize}));");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.MorphologyEx({input1}, {primaryResult}, {op}, kernel_{node.Id});");
            sb.AppendLine($"        kernel_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "Structuring Element", "GetStructuringElement"))
        {
            var shape = GetPropStr(props, "Shape", "ElementShape");
            if (string.IsNullOrEmpty(shape)) shape = "MorphShapes.Rect";
            else if (!shape.Contains(".")) shape = $"MorphShapes.{shape}";
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"        var {primaryResult} = Cv2.GetStructuringElement({shape}, new Size({ksize}, {ksize}));");
            matVars.Add(primaryResult);
        }

        // =====================================================================
        // THRESHOLD
        // =====================================================================
        else if (MatchName(name, "Threshold"))
        {
            var thresh = GetPropDouble(props, "ThresholdValue", "Thresh", "Value", 127);
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            var thType = GetPropStr(props, "ThresholdType", "Type");
            if (string.IsNullOrEmpty(thType)) thType = "ThresholdTypes.Binary";
            else if (!thType.Contains(".")) thType = $"ThresholdTypes.{thType}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Threshold({input1}, {primaryResult}, {thresh}, {maxVal}, {thType});");
        }
        else if (MatchName(name, "Adaptive Threshold", "AdaptiveThreshold"))
        {
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            var method = GetPropStr(props, "AdaptiveMethod", "Method");
            if (string.IsNullOrEmpty(method)) method = "AdaptiveThresholdTypes.GaussianC";
            else if (!method.Contains(".")) method = $"AdaptiveThresholdTypes.{method}";
            var thType = GetPropStr(props, "ThresholdType", "Type");
            if (string.IsNullOrEmpty(thType)) thType = "ThresholdTypes.Binary";
            else if (!thType.Contains(".")) thType = $"ThresholdTypes.{thType}";
            var blockSize = GetPropNum(props, "BlockSize", 11);
            var c = GetPropDouble(props, "C", "Constant", 2);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.AdaptiveThreshold({input1}, {primaryResult}, {maxVal}, {method}, {thType}, {blockSize}, {c});");
        }
        else if (MatchName(name, "OTSU", "Otsu Threshold", "Otsu"))
        {
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Threshold({input1}, {primaryResult}, 0, {maxVal}, ThresholdTypes.Binary | ThresholdTypes.Otsu);");
        }

        // =====================================================================
        // CONTOUR
        // =====================================================================
        else if (MatchName(name, "Find Contours", "FindContours"))
        {
            var mode = GetPropStr(props, "RetrievalMode", "Mode");
            if (string.IsNullOrEmpty(mode)) mode = "RetrievalModes.External";
            else if (!mode.Contains(".")) mode = $"RetrievalModes.{mode}";
            var approx = GetPropStr(props, "ApproximationMethod", "Method");
            if (string.IsNullOrEmpty(approx)) approx = "ContourApproximationModes.ApproxSimple";
            else if (!approx.Contains(".")) approx = $"ContourApproximationModes.{approx}";
            sb.AppendLine($"        Cv2.FindContours({input1}, out Point[][] {primaryResult}, out HierarchyIndex[] hierarchy_{node.Id}, {mode}, {approx});");
        }
        else if (MatchName(name, "Draw Contours", "DrawContours"))
        {
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            var idx = GetPropNum(props, "ContourIndex", "Index", -1);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.DrawContours({primaryResult}, {input2}, {idx}, {color}, {thickness});");
        }
        else if (MatchName(name, "Contour Area", "ContourArea"))
        {
            sb.AppendLine($"        var {primaryResult} = Cv2.ContourArea({input1});");
        }
        else if (MatchName(name, "Approx Poly", "ApproxPolyDP", "Approximate Polygon"))
        {
            var epsilon = GetPropDouble(props, "Epsilon", "Accuracy", 0.02);
            var closed = GetPropBool(props, "Closed", true);
            sb.AppendLine($"        var arcLen_{node.Id} = Cv2.ArcLength({input1}, {CBool(closed)});");
            sb.AppendLine($"        var {primaryResult} = Cv2.ApproxPolyDP({input1}, {epsilon} * arcLen_{node.Id}, {CBool(closed)});");
        }
        else if (MatchName(name, "Convex Hull", "ConvexHull"))
        {
            sb.AppendLine($"        var {primaryResult} = Cv2.ConvexHull({input1});");
        }
        else if (MatchName(name, "Bounding Rect", "BoundingRect", "Bounding Rectangle"))
        {
            sb.AppendLine($"        var {primaryResult} = Cv2.BoundingRect({input1});");
        }
        else if (MatchName(name, "Min Enclosing Circle", "MinEnclosingCircle"))
        {
            if (resultVars.Count >= 2)
            {
                sb.AppendLine($"        Cv2.MinEnclosingCircle({input1}, out Point2f {resultVars[0]}, out float {resultVars[1]});");
            }
            else
            {
                sb.AppendLine($"        Cv2.MinEnclosingCircle({input1}, out Point2f {primaryResult}_center, out float {primaryResult}_radius);");
            }
        }

        // =====================================================================
        // FEATURE DETECTION
        // =====================================================================
        else if (MatchName(name, "ORB", "ORB Detector"))
        {
            var nFeatures = GetPropNum(props, "NFeatures", "MaxFeatures", 500);
            sb.AppendLine($"        using var orb_{node.Id} = ORB.Create(nFeatures: {nFeatures});");
            if (resultVars.Count >= 2)
            {
                sb.AppendLine($"        var {resultVars[1]} = new Mat();");
                matVars.Add(resultVars[1]);
                sb.AppendLine($"        orb_{node.Id}.DetectAndCompute({input1}, null, out KeyPoint[] {resultVars[0]}, {resultVars[1]});");
            }
            else
            {
                sb.AppendLine($"        var desc_{node.Id} = new Mat();");
                sb.AppendLine($"        orb_{node.Id}.DetectAndCompute({input1}, null, out KeyPoint[] {primaryResult}, desc_{node.Id});");
            }
        }
        else if (MatchName(name, "SIFT", "SIFT Detector"))
        {
            var nFeatures = GetPropNum(props, "NFeatures", "MaxFeatures", 0);
            sb.AppendLine($"        using var sift_{node.Id} = SIFT.Create(nFeatures: {nFeatures});");
            if (resultVars.Count >= 2)
            {
                sb.AppendLine($"        var {resultVars[1]} = new Mat();");
                matVars.Add(resultVars[1]);
                sb.AppendLine($"        sift_{node.Id}.DetectAndCompute({input1}, null, out KeyPoint[] {resultVars[0]}, {resultVars[1]});");
            }
            else
            {
                sb.AppendLine($"        var desc_{node.Id} = new Mat();");
                sb.AppendLine($"        sift_{node.Id}.DetectAndCompute({input1}, null, out KeyPoint[] {primaryResult}, desc_{node.Id});");
            }
        }
        else if (MatchName(name, "FAST", "FAST Detector"))
        {
            var threshold = GetPropNum(props, "Threshold", 10);
            sb.AppendLine($"        using var fast_{node.Id} = FastFeatureDetector.Create(threshold: {threshold});");
            sb.AppendLine($"        var {primaryResult} = fast_{node.Id}.Detect({input1});");
        }
        else if (MatchName(name, "Harris Corner", "CornerHarris", "Harris"))
        {
            var blockSize = GetPropNum(props, "BlockSize", 2);
            var ksize = GetPropNum(props, "KernelSize", "Ksize", "ApertureSize", 3);
            var k = GetPropDouble(props, "K", "HarrisK", 0.04);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.CornerHarris({input1}, {primaryResult}, {blockSize}, {ksize}, {k});");
        }
        else if (MatchName(name, "Good Features", "GoodFeaturesToTrack", "Shi-Tomasi"))
        {
            var maxCorners = GetPropNum(props, "MaxCorners", 25);
            var quality = GetPropDouble(props, "QualityLevel", "Quality", 0.01);
            var minDist = GetPropDouble(props, "MinDistance", 10);
            sb.AppendLine($"        var {primaryResult} = Cv2.GoodFeaturesToTrack({input1}, {maxCorners}, {quality}, {minDist});");
        }
        else if (MatchName(name, "BF Matcher", "BFMatcher", "Brute Force Matcher"))
        {
            var normType = GetPropStr(props, "NormType", "Norm");
            if (string.IsNullOrEmpty(normType)) normType = "NormTypes.Hamming";
            else if (!normType.Contains(".")) normType = $"NormTypes.{normType}";
            sb.AppendLine($"        using var bf_{node.Id} = new BFMatcher({normType}, crossCheck: true);");
            sb.AppendLine($"        var {primaryResult} = bf_{node.Id}.Match({input1}, {input2});");
        }

        // =====================================================================
        // DRAWING
        // =====================================================================
        else if (MatchName(name, "Draw Line", "Line"))
        {
            var pt1 = GetPropStr(props, "Point1", "Start", "Pt1");
            var pt2 = GetPropStr(props, "Point2", "End", "Pt2");
            if (string.IsNullOrEmpty(pt1)) pt1 = "new Point(0, 0)";
            if (string.IsNullOrEmpty(pt2)) pt2 = "new Point(100, 100)";
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Line({primaryResult}, {pt1}, {pt2}, {color}, {thickness});");
        }
        else if (MatchName(name, "Draw Rectangle", "Rectangle"))
        {
            var pt1 = GetPropStr(props, "Point1", "TopLeft", "Pt1");
            var pt2 = GetPropStr(props, "Point2", "BottomRight", "Pt2");
            if (string.IsNullOrEmpty(pt1)) pt1 = "new Point(0, 0)";
            if (string.IsNullOrEmpty(pt2)) pt2 = "new Point(100, 100)";
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Rectangle({primaryResult}, {pt1}, {pt2}, {color}, {thickness});");
        }
        else if (MatchName(name, "Draw Circle", "Circle"))
        {
            var center = GetPropStr(props, "Center");
            if (string.IsNullOrEmpty(center)) center = "new Point(50, 50)";
            var radius = GetPropNum(props, "Radius", 25);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Circle({primaryResult}, {center}, {radius}, {color}, {thickness});");
        }
        else if (MatchName(name, "Draw Ellipse", "Ellipse"))
        {
            var center = GetPropStr(props, "Center");
            if (string.IsNullOrEmpty(center)) center = "new Point(50, 50)";
            var axes = GetPropStr(props, "Axes", "Size");
            if (string.IsNullOrEmpty(axes)) axes = "new Size(25, 15)";
            var angle = GetPropDouble(props, "Angle", 0);
            var startAngle = GetPropDouble(props, "StartAngle", 0);
            var endAngle = GetPropDouble(props, "EndAngle", 360);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Ellipse({primaryResult}, {center}, {axes}, {angle}, {startAngle}, {endAngle}, {color}, {thickness});");
        }
        else if (MatchName(name, "Put Text", "PutText", "Draw Text", "Text"))
        {
            var text = GetPropStr(props, "Text", "Content");
            if (string.IsNullOrEmpty(text)) text = "Hello";
            var org = GetPropStr(props, "Origin", "Position", "Org");
            if (string.IsNullOrEmpty(org)) org = "new Point(10, 30)";
            var font = GetPropStr(props, "Font", "FontFace");
            if (string.IsNullOrEmpty(font)) font = "HersheyFonts.HersheySimplex";
            else if (!font.Contains(".")) font = $"HersheyFonts.{font}";
            var scale = GetPropDouble(props, "FontScale", "Scale", 1.0);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(255, 255, 255)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.PutText({primaryResult}, {CStr(text)}, {org}, {font}, {scale}, {color}, {thickness});");
        }
        else if (MatchName(name, "Polylines", "Draw Polylines"))
        {
            var isClosed = GetPropBool(props, "IsClosed", "Closed", true);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "new Scalar(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Polylines({primaryResult}, new[] {{ {input2} }}, {CBool(isClosed)}, {color}, {thickness});");
        }

        // =====================================================================
        // TRANSFORM
        // =====================================================================
        else if (MatchName(name, "Resize"))
        {
            var width = GetPropNum(props, "Width", 640);
            var height = GetPropNum(props, "Height", 480);
            var interp = GetPropStr(props, "Interpolation");
            if (string.IsNullOrEmpty(interp)) interp = "InterpolationFlags.Linear";
            else if (!interp.Contains(".")) interp = $"InterpolationFlags.{interp}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Resize({input1}, {primaryResult}, new Size({width}, {height}), interpolation: {interp});");
        }
        else if (MatchName(name, "Rotate"))
        {
            var rotCode = GetPropStr(props, "RotateCode", "Code", "Rotation");
            if (string.IsNullOrEmpty(rotCode)) rotCode = "RotateFlags.Rotate90Clockwise";
            else if (!rotCode.Contains(".")) rotCode = $"RotateFlags.{rotCode}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Rotate({input1}, {primaryResult}, {rotCode});");
        }
        else if (MatchName(name, "Crop", "ROI", "Region of Interest"))
        {
            var x = GetPropNum(props, "X", 0);
            var y = GetPropNum(props, "Y", 0);
            var w = GetPropNum(props, "Width", "W", 100);
            var h = GetPropNum(props, "Height", "H", 100);
            sb.AppendLine($"        var {primaryResult} = new Mat({input1}, new Rect({x}, {y}, {w}, {h}));");
            matVars.Add(primaryResult);
        }
        else if (MatchName(name, "Flip"))
        {
            var flipCode = GetPropNum(props, "FlipCode", "Code", 1);
            var flipMode = flipCode switch
            {
                0 => "FlipMode.X",
                1 => "FlipMode.Y",
                _ => "FlipMode.XY"
            };
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Flip({input1}, {primaryResult}, {flipMode});");
        }
        else if (MatchName(name, "Warp Affine", "WarpAffine", "Affine Transform"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.WarpAffine({input1}, {primaryResult}, {input2}, {input1}.Size());");
        }
        else if (MatchName(name, "Warp Perspective", "WarpPerspective", "Perspective Transform"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.WarpPerspective({input1}, {primaryResult}, {input2}, {input1}.Size());");
        }
        else if (MatchName(name, "Remap"))
        {
            var interp = GetPropStr(props, "Interpolation");
            if (string.IsNullOrEmpty(interp)) interp = "InterpolationFlags.Linear";
            else if (!interp.Contains(".")) interp = $"InterpolationFlags.{interp}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Remap({input1}, {primaryResult}, {input2}, {GetInput(inputVarMap, node, 2)}, {interp});");
        }

        // =====================================================================
        // HISTOGRAM
        // =====================================================================
        else if (MatchName(name, "Calc Histogram", "CalcHist", "Calculate Histogram"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.CalcHist(new[] {{ {input1} }}, new[] {{ 0 }}, null, {primaryResult}, 1, new[] {{ 256 }}, new[] {{ new Rangef(0, 256) }});");
        }
        else if (MatchName(name, "Equalize Histogram", "EqualizeHist", "Histogram Equalization"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.EqualizeHist({input1}, {primaryResult});");
        }
        else if (MatchName(name, "CLAHE"))
        {
            var clipLimit = GetPropDouble(props, "ClipLimit", 2.0);
            var tileGridSizeX = GetPropNum(props, "TileGridSizeX", "TileGridSize", 8);
            var tileGridSizeY = GetPropNum(props, "TileGridSizeY", "TileGridSize", 8);
            sb.AppendLine($"        using var clahe_{node.Id} = Cv2.CreateCLAHE(clipLimit: {clipLimit}, tileGridSize: new Size({tileGridSizeX}, {tileGridSizeY}));");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        clahe_{node.Id}.Apply({input1}, {primaryResult});");
        }

        // =====================================================================
        // ARITHMETIC
        // =====================================================================
        else if (MatchName(name, "Add"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Add({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Subtract"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Subtract({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Multiply"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Multiply({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Absolute Difference", "AbsDiff"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Absdiff({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Bitwise AND", "BitwiseAnd"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.BitwiseAnd({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Bitwise OR", "BitwiseOr"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.BitwiseOr({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Bitwise XOR", "BitwiseXor"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.BitwiseXor({input1}, {input2}, {primaryResult});");
        }
        else if (MatchName(name, "Bitwise NOT", "BitwiseNot"))
        {
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.BitwiseNot({input1}, {primaryResult});");
        }
        else if (MatchName(name, "Blend", "Weighted Add", "AddWeighted"))
        {
            var alpha = GetPropDouble(props, "Alpha", "Weight1", 0.5);
            var beta = GetPropDouble(props, "Beta", "Weight2", 0.5);
            var gamma = GetPropDouble(props, "Gamma", 0);
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.AddWeighted({input1}, {alpha}, {input2}, {beta}, {gamma}, {primaryResult});");
        }

        // =====================================================================
        // DETECTION
        // =====================================================================
        else if (MatchName(name, "Hough Lines", "HoughLines"))
        {
            var rho = GetPropDouble(props, "Rho", 1);
            var theta = GetPropDouble(props, "Theta", Math.PI / 180);
            var threshold = GetPropNum(props, "Threshold", 150);
            sb.AppendLine($"        var {primaryResult} = Cv2.HoughLines({input1}, {rho}, {theta}, {threshold});");
        }
        else if (MatchName(name, "Hough Lines P", "HoughLinesP", "Probabilistic Hough"))
        {
            var rho = GetPropDouble(props, "Rho", 1);
            var theta = GetPropDouble(props, "Theta", Math.PI / 180);
            var threshold = GetPropNum(props, "Threshold", 50);
            var minLineLength = GetPropDouble(props, "MinLineLength", 50);
            var maxLineGap = GetPropDouble(props, "MaxLineGap", 10);
            sb.AppendLine($"        var {primaryResult} = Cv2.HoughLinesP({input1}, {rho}, {theta}, {threshold}, {minLineLength}, {maxLineGap});");
        }
        else if (MatchName(name, "Hough Circles", "HoughCircles"))
        {
            var method = GetPropStr(props, "Method");
            if (string.IsNullOrEmpty(method)) method = "HoughModes.Gradient";
            else if (!method.Contains(".")) method = $"HoughModes.{method}";
            var dp = GetPropDouble(props, "Dp", "DP", 1);
            var minDist = GetPropDouble(props, "MinDist", "MinDistance", 20);
            sb.AppendLine($"        var {primaryResult} = Cv2.HoughCircles({input1}, {method}, {dp}, {minDist});");
        }
        else if (MatchName(name, "Match Template", "MatchTemplate", "Template Match"))
        {
            var method = GetPropStr(props, "Method", "MatchMethod");
            if (string.IsNullOrEmpty(method)) method = "TemplateMatchModes.CCoeffNormed";
            else if (!method.Contains(".")) method = $"TemplateMatchModes.{method}";
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.MatchTemplate({input1}, {input2}, {primaryResult}, {method});");
        }
        else if (MatchName(name, "Cascade Classifier", "CascadeClassifier", "Haar Cascade"))
        {
            var cascadePath = GetPropStr(props, "CascadePath", "Path", "FilePath");
            if (string.IsNullOrEmpty(cascadePath)) cascadePath = "haarcascade_frontalface_default.xml";
            var scaleFactor = GetPropDouble(props, "ScaleFactor", 1.1);
            var minNeighbors = GetPropNum(props, "MinNeighbors", 5);
            sb.AppendLine($"        using var cascade_{node.Id} = new CascadeClassifier({CStr(cascadePath)});");
            sb.AppendLine($"        var {primaryResult} = cascade_{node.Id}.DetectMultiScale({input1}, scaleFactor: {scaleFactor}, minNeighbors: {minNeighbors});");
        }

        // =====================================================================
        // SEGMENTATION
        // =====================================================================
        else if (MatchName(name, "GrabCut"))
        {
            var iterCount = GetPropNum(props, "IterCount", "Iterations", 5);
            var mode = GetPropStr(props, "Mode");
            if (string.IsNullOrEmpty(mode)) mode = "GrabCutModes.InitWithRect";
            else if (!mode.Contains(".")) mode = $"GrabCutModes.{mode}";
            sb.AppendLine($"        var mask_{node.Id} = new Mat({input1}.Rows, {input1}.Cols, MatType.CV_8UC1, Scalar.All(0));");
            sb.AppendLine($"        var bgModel_{node.Id} = new Mat();");
            sb.AppendLine($"        var fgModel_{node.Id} = new Mat();");
            sb.AppendLine($"        var rect_{node.Id} = new Rect(50, 50, {input1}.Cols - 100, {input1}.Rows - 100); // Adjust as needed");
            sb.AppendLine($"        Cv2.GrabCut({input1}, mask_{node.Id}, rect_{node.Id}, bgModel_{node.Id}, fgModel_{node.Id}, {iterCount}, {mode});");
            sb.AppendLine($"        var {primaryResult} = new Mat();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Compare(mask_{node.Id}, new Scalar(3), {primaryResult}, CmpType.EQ);");
            sb.AppendLine($"        mask_{node.Id}.Dispose();");
            sb.AppendLine($"        bgModel_{node.Id}.Dispose();");
            sb.AppendLine($"        fgModel_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "Watershed"))
        {
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.Watershed({primaryResult}, {input2});");
        }
        else if (MatchName(name, "Flood Fill", "FloodFill"))
        {
            var seedPoint = GetPropStr(props, "SeedPoint", "Seed");
            if (string.IsNullOrEmpty(seedPoint)) seedPoint = "new Point(0, 0)";
            var newVal = GetPropStr(props, "NewValue", "NewVal");
            if (string.IsNullOrEmpty(newVal)) newVal = "new Scalar(255, 255, 255)";
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        Cv2.FloodFill({primaryResult}, {seedPoint}, {newVal});");
        }

        // =====================================================================
        // VALUE NODES
        // =====================================================================
        else if (MatchName(name, "Integer", "Int Value", "Integer Value"))
        {
            var val = GetPropNum(props, "Value", 0);
            sb.AppendLine($"        var {primaryResult} = {val};");
        }
        else if (MatchName(name, "Float", "Float Value", "Double", "Double Value"))
        {
            var val = GetPropDouble(props, "Value", 0.0);
            sb.AppendLine($"        var {primaryResult} = {val};");
        }
        else if (MatchName(name, "Boolean", "Bool Value", "Boolean Value"))
        {
            var val = GetPropBool(props, "Value", false);
            sb.AppendLine($"        var {primaryResult} = {CBool(val)};");
        }
        else if (MatchName(name, "String", "String Value", "Text Value"))
        {
            var val = GetPropStr(props, "Value");
            sb.AppendLine($"        var {primaryResult} = {CStr(val)};");
        }
        else if (MatchName(name, "Point", "Point Value"))
        {
            var x = GetPropNum(props, "X", 0);
            var y = GetPropNum(props, "Y", 0);
            sb.AppendLine($"        var {primaryResult} = new Point({x}, {y});");
        }
        else if (MatchName(name, "Size", "Size Value"))
        {
            var w = GetPropNum(props, "Width", "W", 640);
            var h = GetPropNum(props, "Height", "H", 480);
            sb.AppendLine($"        var {primaryResult} = new Size({w}, {h});");
        }
        else if (MatchName(name, "Scalar", "Scalar Value", "Color Value"))
        {
            var b = GetPropNum(props, "B", "Blue", 0);
            var g = GetPropNum(props, "G", "Green", 0);
            var r = GetPropNum(props, "R", "Red", 0);
            sb.AppendLine($"        var {primaryResult} = new Scalar({b}, {g}, {r});");
        }

        // =====================================================================
        // CONTROL
        // =====================================================================
        else if (MatchName(name, "If", "If/Else", "Branch", "Condition"))
        {
            sb.AppendLine($"        object {primaryResult};");
            sb.AppendLine($"        if (Convert.ToBoolean({input1}))");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            {primaryResult} = {input2}; // True branch");
            sb.AppendLine($"        }}");
            if (node.Inputs.Count > 2)
            {
                var input3 = GetInput(inputVarMap, node, 2);
                sb.AppendLine($"        else");
                sb.AppendLine($"        {{");
                sb.AppendLine($"            {primaryResult} = {input3}; // False branch");
                sb.AppendLine($"        }}");
            }
        }
        else if (MatchName(name, "For Loop", "Loop", "For"))
        {
            var count = GetPropNum(props, "Count", "Iterations", 10);
            sb.AppendLine($"        var {primaryResult} = 0;");
            sb.AppendLine($"        for (int i_{node.Id} = 0; i_{node.Id} < {count}; i_{node.Id}++)");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            {primaryResult} = i_{node.Id}; // Loop variable");
            sb.AppendLine($"            // Add loop body here");
            sb.AppendLine($"        }}");
        }
        else if (MatchName(name, "Switch", "Select"))
        {
            sb.AppendLine($"        object? {primaryResult} = null;");
            sb.AppendLine($"        switch ({input1})");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            // Add cases here");
            sb.AppendLine($"            default:");
            sb.AppendLine($"                {primaryResult} = null;");
            sb.AppendLine($"                break;");
            sb.AppendLine($"        }}");
        }

        // =====================================================================
        // COMMUNICATION
        // =====================================================================
        else if (MatchName(name, "TCP Client", "Socket Client", "TCP Send"))
        {
            var host = GetPropStr(props, "Host", "Address");
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            var port = GetPropNum(props, "Port", 5000);
            sb.AppendLine($"        string {primaryResult};");
            sb.AppendLine($"        using (var client_{node.Id} = new TcpClient({CStr(host)}, {port}))");
            sb.AppendLine($"        using (var stream_{node.Id} = client_{node.Id}.GetStream())");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var sendData_{node.Id} = System.Text.Encoding.UTF8.GetBytes({input1}?.ToString() ?? \"\");");
            sb.AppendLine($"            stream_{node.Id}.Write(sendData_{node.Id}, 0, sendData_{node.Id}.Length);");
            sb.AppendLine($"            var buffer_{node.Id} = new byte[4096];");
            sb.AppendLine($"            var bytesRead_{node.Id} = stream_{node.Id}.Read(buffer_{node.Id}, 0, buffer_{node.Id}.Length);");
            sb.AppendLine($"            {primaryResult} = System.Text.Encoding.UTF8.GetString(buffer_{node.Id}, 0, bytesRead_{node.Id});");
            sb.AppendLine($"        }}");
        }
        else if (MatchName(name, "TCP Server", "Socket Server", "TCP Listen"))
        {
            var host = GetPropStr(props, "Host", "Address");
            if (string.IsNullOrEmpty(host)) host = "0.0.0.0";
            var port = GetPropNum(props, "Port", 5000);
            sb.AppendLine($"        string {primaryResult};");
            sb.AppendLine($"        var listener_{node.Id} = new TcpListener(IPAddress.Parse({CStr(host)}), {port});");
            sb.AppendLine($"        listener_{node.Id}.Start();");
            sb.AppendLine($"        using (var client_{node.Id} = listener_{node.Id}.AcceptTcpClient())");
            sb.AppendLine($"        using (var stream_{node.Id} = client_{node.Id}.GetStream())");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var buffer_{node.Id} = new byte[4096];");
            sb.AppendLine($"            var bytesRead_{node.Id} = stream_{node.Id}.Read(buffer_{node.Id}, 0, buffer_{node.Id}.Length);");
            sb.AppendLine($"            {primaryResult} = System.Text.Encoding.UTF8.GetString(buffer_{node.Id}, 0, bytesRead_{node.Id});");
            sb.AppendLine($"        }}");
            sb.AppendLine($"        listener_{node.Id}.Stop();");
        }
        else if (MatchName(name, "Serial", "Serial Port", "Serial Read", "Serial Write"))
        {
            var portName = GetPropStr(props, "PortName", "Port", "COM");
            if (string.IsNullOrEmpty(portName)) portName = "COM3";
            var baudRate = GetPropNum(props, "BaudRate", "Baud", 9600);
            sb.AppendLine($"        string {primaryResult};");
            sb.AppendLine($"        using (var ser_{node.Id} = new SerialPort({CStr(portName)}, {baudRate}))");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            ser_{node.Id}.Open();");
            if (MatchName(name, "Serial Write"))
            {
                sb.AppendLine($"            ser_{node.Id}.Write({input1}?.ToString() ?? \"\");");
                sb.AppendLine($"            {primaryResult} = \"sent\";");
            }
            else
            {
                sb.AppendLine($"            {primaryResult} = ser_{node.Id}.ReadLine();");
            }
            sb.AppendLine($"            ser_{node.Id}.Close();");
            sb.AppendLine($"        }}");
        }

        // =====================================================================
        // DATA PROCESSING
        // =====================================================================
        else if (MatchName(name, "String Parse", "Parse String", "String Split"))
        {
            var delimiter = GetPropStr(props, "Delimiter", "Separator");
            if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
            sb.AppendLine($"        var {primaryResult} = ({input1}?.ToString() ?? \"\").Split({CStr(delimiter)});");
        }
        else if (MatchName(name, "CSV Read", "Read CSV", "CSV Load"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"        var {primaryResult} = File.ReadAllLines({CStr(path)})");
            sb.AppendLine($"            .Select(line => line.Split(',')).ToList();");
        }
        else if (MatchName(name, "CSV Write", "Write CSV", "CSV Save"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"        var lines_{node.Id} = ((IEnumerable<string[]>){input1}).Select(row => string.Join(\",\", row));");
            sb.AppendLine($"        File.WriteAllLines({CStr(path)}, lines_{node.Id});");
            sb.AppendLine($"        var {primaryResult} = true;");
        }
        else if (MatchName(name, "JSON Parse", "Parse JSON"))
        {
            sb.AppendLine($"        var {primaryResult} = System.Text.Json.JsonSerializer.Deserialize<object>({input1}?.ToString() ?? \"{{}}\");");
        }
        else if (MatchName(name, "JSON Stringify", "To JSON"))
        {
            sb.AppendLine($"        var {primaryResult} = System.Text.Json.JsonSerializer.Serialize({input1});");
        }

        // =====================================================================
        // MEDIAPIPE (via ONNX Runtime - standalone C# equivalent)
        // =====================================================================
        else if (MatchName(name, "MP Face Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            sb.AppendLine($"        // MediaPipe Face Detection via ONNX Runtime");
            sb.AppendLine($"        // Requires: face_detection_short_range.onnx in working directory");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var session_{node.Id} = new InferenceSession(\"Models/MediaPipe/face_detection_short_range.onnx\");");
            sb.AppendLine($"        var resized_{node.Id} = new Mat();");
            sb.AppendLine($"        Cv2.Resize({input1}, resized_{node.Id}, new Size(128, 128));");
            sb.AppendLine($"        // Preprocess: BGR→RGB, normalize [0,1], NHWC float32 tensor");
            sb.AppendLine($"        var rgb_{node.Id} = new Mat();");
            sb.AppendLine($"        Cv2.CvtColor(resized_{node.Id}, rgb_{node.Id}, ColorConversionCodes.BGR2RGB);");
            sb.AppendLine($"        var data_{node.Id} = new float[1 * 128 * 128 * 3];");
            sb.AppendLine($"        rgb_{node.Id}.ConvertTo(rgb_{node.Id}, MatType.CV_32FC3, 1.0 / 255.0);");
            sb.AppendLine($"        System.Runtime.InteropServices.Marshal.Copy(rgb_{node.Id}.Data, data_{node.Id}, 0, data_{node.Id}.Length);");
            sb.AppendLine($"        var tensor_{node.Id} = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data_{node.Id}, new[] {{ 1, 128, 128, 3 }});");
            sb.AppendLine($"        var inputs_{node.Id} = new[] {{ NamedOnnxValue.CreateFromTensor(session_{node.Id}.InputNames[0], tensor_{node.Id}) }};");
            sb.AppendLine($"        using var results_{node.Id} = session_{node.Id}.Run(inputs_{node.Id});");
            sb.AppendLine($"        // Post-process detections: decode boxes, apply NMS, draw on {primaryResult}");
            sb.AppendLine($"        Console.WriteLine($\"Face detection: {{results_{node.Id}.Count}} output tensors\");");
            if (resultVars.Count > 3) sb.AppendLine($"        var {resultVars[3]} = 0; // count - implement post-processing");
            sb.AppendLine($"        resized_{node.Id}.Dispose(); rgb_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "MP Face Mesh"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            sb.AppendLine($"        // MediaPipe Face Mesh via ONNX Runtime");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var session_{node.Id} = new InferenceSession(\"Models/MediaPipe/face_landmark.onnx\");");
            sb.AppendLine($"        var resized_{node.Id} = new Mat(); Cv2.Resize({input1}, resized_{node.Id}, new Size(192, 192));");
            sb.AppendLine($"        var rgb_{node.Id} = new Mat(); Cv2.CvtColor(resized_{node.Id}, rgb_{node.Id}, ColorConversionCodes.BGR2RGB);");
            sb.AppendLine($"        var data_{node.Id} = new float[1 * 192 * 192 * 3];");
            sb.AppendLine($"        rgb_{node.Id}.ConvertTo(rgb_{node.Id}, MatType.CV_32FC3, 1.0 / 255.0);");
            sb.AppendLine($"        System.Runtime.InteropServices.Marshal.Copy(rgb_{node.Id}.Data, data_{node.Id}, 0, data_{node.Id}.Length);");
            sb.AppendLine($"        var tensor_{node.Id} = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data_{node.Id}, new[] {{ 1, 192, 192, 3 }});");
            sb.AppendLine($"        var inputs_{node.Id} = new[] {{ NamedOnnxValue.CreateFromTensor(session_{node.Id}.InputNames[0], tensor_{node.Id}) }};");
            sb.AppendLine($"        using var results_{node.Id} = session_{node.Id}.Run(inputs_{node.Id});");
            sb.AppendLine($"        Console.WriteLine($\"Face Mesh: {{results_{node.Id}.Count}} output tensors (468 landmarks)\");");
            if (resultVars.Count > 2) sb.AppendLine($"        var {resultVars[2]} = 0; // count - implement landmark extraction");
            sb.AppendLine($"        resized_{node.Id}.Dispose(); rgb_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "MP Hand Landmark"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var maxHands = GetPropNum(props, "MaxHands", 2);
            sb.AppendLine($"        // MediaPipe Hand Landmark via ONNX Runtime (2-stage: palm detection + hand landmark)");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var palmSess_{node.Id} = new InferenceSession(\"Models/MediaPipe/palm_detection.onnx\");");
            sb.AppendLine($"        using var handSess_{node.Id} = new InferenceSession(\"Models/MediaPipe/hand_landmark.onnx\");");
            sb.AppendLine($"        // Stage 1: Palm detection (192x192) → detect hand ROI");
            sb.AppendLine($"        // Stage 2: Hand landmark (224x224) → 21 keypoints");
            sb.AppendLine($"        Console.WriteLine(\"Hand Landmark: implement 2-stage pipeline for {maxHands} hand(s)\");");
            if (resultVars.Count > 2) sb.AppendLine($"        var {resultVars[2]} = 0; // count");
        }
        else if (MatchName(name, "MP Pose Landmark"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            sb.AppendLine($"        // MediaPipe Pose Landmark via ONNX Runtime (2-stage)");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var poseDet_{node.Id} = new InferenceSession(\"Models/MediaPipe/pose_detection.onnx\");");
            sb.AppendLine($"        using var poseLM_{node.Id} = new InferenceSession(\"Models/MediaPipe/pose_landmark_full.onnx\");");
            sb.AppendLine($"        // Stage 1: Pose detection (224x224) → body ROI");
            sb.AppendLine($"        // Stage 2: Pose landmark (256x256) → 33 keypoints");
            sb.AppendLine($"        Console.WriteLine(\"Pose Landmark: implement 2-stage pipeline\");");
            if (resultVars.Count > 3) sb.AppendLine($"        var {resultVars[3]} = 0; // count");
        }
        else if (MatchName(name, "MP Selfie Segmentation"))
        {
            var threshold = GetPropDouble(props, "Threshold", 0.5);
            var bgMode = GetPropStr(props, "BackgroundMode");
            var blurStrength = GetPropNum(props, "BlurStrength", 21);
            sb.AppendLine($"        // MediaPipe Selfie Segmentation via ONNX Runtime");
            sb.AppendLine($"        using var session_{node.Id} = new InferenceSession(\"Models/MediaPipe/selfie_segmentation.onnx\");");
            sb.AppendLine($"        var resized_{node.Id} = new Mat(); Cv2.Resize({input1}, resized_{node.Id}, new Size(256, 256));");
            sb.AppendLine($"        var rgb_{node.Id} = new Mat(); Cv2.CvtColor(resized_{node.Id}, rgb_{node.Id}, ColorConversionCodes.BGR2RGB);");
            sb.AppendLine($"        var data_{node.Id} = new float[1 * 256 * 256 * 3];");
            sb.AppendLine($"        rgb_{node.Id}.ConvertTo(rgb_{node.Id}, MatType.CV_32FC3, 1.0 / 255.0);");
            sb.AppendLine($"        System.Runtime.InteropServices.Marshal.Copy(rgb_{node.Id}.Data, data_{node.Id}, 0, data_{node.Id}.Length);");
            sb.AppendLine($"        var tensor_{node.Id} = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data_{node.Id}, new[] {{ 1, 256, 256, 3 }});");
            sb.AppendLine($"        var inputs_{node.Id} = new[] {{ NamedOnnxValue.CreateFromTensor(session_{node.Id}.InputNames[0], tensor_{node.Id}) }};");
            sb.AppendLine($"        using var results_{node.Id} = session_{node.Id}.Run(inputs_{node.Id});");
            sb.AppendLine($"        var maskData_{node.Id} = results_{node.Id}.First().AsEnumerable<float>().ToArray();");
            sb.AppendLine($"        // Build mask and apply background effect");
            sb.AppendLine($"        var maskMat_{node.Id} = new Mat(256, 256, MatType.CV_32FC1, maskData_{node.Id});");
            sb.AppendLine($"        var maskResized_{node.Id} = new Mat(); Cv2.Resize(maskMat_{node.Id}, maskResized_{node.Id}, {input1}.Size());");
            sb.AppendLine($"        var mask8_{node.Id} = new Mat(); maskResized_{node.Id}.ConvertTo(mask8_{node.Id}, MatType.CV_8UC1, 255.0);");
            sb.AppendLine($"        Cv2.Threshold(mask8_{node.Id}, mask8_{node.Id}, (int)({threshold} * 255), 255, ThresholdTypes.Binary);");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"        var {resultVars[1]} = mask8_{node.Id}.Clone(); // Mask output");
                matVars.Add(resultVars[1]);
            }
            if (string.IsNullOrEmpty(bgMode) || bgMode.Contains("Blur"))
            {
                sb.AppendLine($"        var bg_{node.Id} = new Mat(); Cv2.GaussianBlur({input1}, bg_{node.Id}, new Size({blurStrength} | 1, {blurStrength} | 1), 0);");
            }
            else if (bgMode.Contains("Green"))
            {
                sb.AppendLine($"        var bg_{node.Id} = new Mat({input1}.Size(), {input1}.Type(), new Scalar(0, 255, 0));");
            }
            else
            {
                sb.AppendLine($"        var bg_{node.Id} = Mat.Zeros({input1}.Size(), {input1}.Type());");
            }
            sb.AppendLine($"        var {primaryResult} = new Mat(); {input1}.CopyTo({primaryResult});");
            matVars.Add(primaryResult);
            sb.AppendLine($"        bg_{node.Id}.CopyTo({primaryResult}, 255 - mask8_{node.Id});");
            sb.AppendLine($"        resized_{node.Id}.Dispose(); rgb_{node.Id}.Dispose(); maskMat_{node.Id}.Dispose(); maskResized_{node.Id}.Dispose(); mask8_{node.Id}.Dispose(); bg_{node.Id}.Dispose();");
        }
        else if (MatchName(name, "MP Object Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var maxDet = GetPropNum(props, "MaxDetections", 20);
            sb.AppendLine($"        // MediaPipe Object Detection via ONNX Runtime (SSD MobileNet)");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var session_{node.Id} = new InferenceSession(\"Models/MediaPipe/ssd_mobilenet_v2.onnx\");");
            sb.AppendLine($"        // Preprocess to 300x300, run inference, decode SSD outputs, draw boxes");
            sb.AppendLine($"        Console.WriteLine(\"Object Detection: implement SSD post-processing\");");
            if (resultVars.Count > 4) sb.AppendLine($"        var {resultVars[4]} = 0; // count");
        }

        // =====================================================================
        // YOLO (via ONNX Runtime)
        // =====================================================================
        else if (MatchName(name, "YOLOv8", "YOLO Detection", "YOLOv8 Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.25);
            var iou = GetPropDouble(props, "IoUThreshold", "IoU", 0.45);
            var modelFile = GetPropStr(props, "ModelFile");
            if (string.IsNullOrEmpty(modelFile)) modelFile = "yolov8n.onnx";
            sb.AppendLine($"        // YOLOv8 Detection via ONNX Runtime");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var session_{node.Id} = new InferenceSession({CStr(modelFile)});");
            sb.AppendLine($"        // Preprocess: resize to 640x640, normalize [0,1], NCHW format");
            sb.AppendLine($"        var resized_{node.Id} = new Mat();");
            sb.AppendLine($"        Cv2.Resize({input1}, resized_{node.Id}, new Size(640, 640));");
            sb.AppendLine($"        var rgb_{node.Id} = new Mat();");
            sb.AppendLine($"        Cv2.CvtColor(resized_{node.Id}, rgb_{node.Id}, ColorConversionCodes.BGR2RGB);");
            sb.AppendLine($"        rgb_{node.Id}.ConvertTo(rgb_{node.Id}, MatType.CV_32FC3, 1.0 / 255.0);");
            sb.AppendLine($"        var pixelData_{node.Id} = new float[1 * 3 * 640 * 640];");
            sb.AppendLine($"        // Convert HWC→CHW");
            sb.AppendLine($"        unsafe {{ var ptr = (float*)rgb_{node.Id}.Data;");
            sb.AppendLine($"            for (int y = 0; y < 640; y++) for (int x = 0; x < 640; x++) for (int c = 0; c < 3; c++)");
            sb.AppendLine($"                pixelData_{node.Id}[c * 640 * 640 + y * 640 + x] = ptr[(y * 640 + x) * 3 + c]; }}");
            sb.AppendLine($"        var tensor_{node.Id} = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(pixelData_{node.Id}, new[] {{ 1, 3, 640, 640 }});");
            sb.AppendLine($"        var inputs_{node.Id} = new[] {{ NamedOnnxValue.CreateFromTensor(session_{node.Id}.InputNames[0], tensor_{node.Id}) }};");
            sb.AppendLine($"        using var results_{node.Id} = session_{node.Id}.Run(inputs_{node.Id});");
            sb.AppendLine($"        var output_{node.Id} = results_{node.Id}.First().AsEnumerable<float>().ToArray();");
            sb.AppendLine($"        // YOLOv8 output: [1, 84, 8400] → transpose, decode cx/cy/w/h + 80 class scores");
            sb.AppendLine($"        var scaleX_{node.Id} = (float){input1}.Width / 640f;");
            sb.AppendLine($"        var scaleY_{node.Id} = (float){input1}.Height / 640f;");
            sb.AppendLine($"        var cocoLabels = new[] {{ \"person\",\"bicycle\",\"car\",\"motorcycle\",\"airplane\",\"bus\",\"train\",\"truck\",\"boat\",\"traffic light\",\"fire hydrant\",\"stop sign\",\"parking meter\",\"bench\",\"bird\",\"cat\",\"dog\",\"horse\",\"sheep\",\"cow\",\"elephant\",\"bear\",\"zebra\",\"giraffe\",\"backpack\",\"umbrella\",\"handbag\",\"tie\",\"suitcase\",\"frisbee\",\"skis\",\"snowboard\",\"sports ball\",\"kite\",\"baseball bat\",\"baseball glove\",\"skateboard\",\"surfboard\",\"tennis racket\",\"bottle\",\"wine glass\",\"cup\",\"fork\",\"knife\",\"spoon\",\"bowl\",\"banana\",\"apple\",\"sandwich\",\"orange\",\"broccoli\",\"carrot\",\"hot dog\",\"pizza\",\"donut\",\"cake\",\"chair\",\"couch\",\"potted plant\",\"bed\",\"dining table\",\"toilet\",\"tv\",\"laptop\",\"mouse\",\"remote\",\"keyboard\",\"cell phone\",\"microwave\",\"oven\",\"toaster\",\"sink\",\"refrigerator\",\"book\",\"clock\",\"vase\",\"scissors\",\"teddy bear\",\"hair drier\",\"toothbrush\" }};");
            sb.AppendLine($"        // Decode 8400 predictions (implement NMS with conf={confidence}, iou={iou})");
            sb.AppendLine($"        Console.WriteLine($\"YOLOv8: {{output_{node.Id}.Length}} output values\");");
            if (resultVars.Count > 4) sb.AppendLine($"        var {resultVars[4]} = 0; // detection count");
            sb.AppendLine($"        resized_{node.Id}.Dispose(); rgb_{node.Id}.Dispose();");
        }

        // =====================================================================
        // OCR
        // =====================================================================
        else if (MatchName(name, "PaddleOCR"))
        {
            var recThreshold = GetPropDouble(props, "RecThreshold", 0.5);
            var detModel = GetPropStr(props, "DetModel");
            if (string.IsNullOrEmpty(detModel)) detModel = "ppocr_det.onnx";
            var recModel = GetPropStr(props, "RecModel");
            if (string.IsNullOrEmpty(recModel)) recModel = "ppocr_rec.onnx";
            sb.AppendLine($"        // PaddleOCR via ONNX Runtime (detection + recognition)");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            sb.AppendLine($"        using var detSession_{node.Id} = new InferenceSession(\"Models/OCR/{detModel}\");");
            sb.AppendLine($"        using var recSession_{node.Id} = new InferenceSession(\"Models/OCR/{recModel}\");");
            sb.AppendLine($"        // Stage 1: Text detection - preprocess and run det model");
            sb.AppendLine($"        // Stage 2: For each detected text region, crop and run rec model");
            sb.AppendLine($"        // Stage 3: CTC decode recognition output using dictionary file");
            sb.AppendLine($"        Console.WriteLine(\"PaddleOCR: implement det+rec pipeline\");");
            if (resultVars.Count > 4) sb.AppendLine($"        var {resultVars[4]} = 0; // count");
            if (resultVars.Count > 5) sb.AppendLine($"        var {resultVars[5]} = \"\"; // full text");
        }
        else if (MatchName(name, "Tesseract OCR", "Tesseract"))
        {
            var lang = GetPropStr(props, "Language");
            if (string.IsNullOrEmpty(lang)) lang = "eng+kor";
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var psm = GetPropNum(props, "PageSegMode", 3);
            sb.AppendLine($"        // Tesseract OCR via Tesseract NuGet package");
            sb.AppendLine($"        // Requires: tessdata folder with language files");
            sb.AppendLine($"        var {primaryResult} = {input1}.Clone();");
            matVars.Add(primaryResult);
            if (resultVars.Count > 1) sb.AppendLine($"        var {resultVars[1]} = new List<string>(); // texts");
            if (resultVars.Count > 2) sb.AppendLine($"        var {resultVars[2]} = new List<Rect>(); // boxes");
            if (resultVars.Count > 3) sb.AppendLine($"        var {resultVars[3]} = new List<double>(); // scores");
            if (resultVars.Count > 4) sb.AppendLine($"        var {resultVars[4]} = 0; // count");
            if (resultVars.Count > 5) sb.AppendLine($"        var {resultVars[5]} = \"\"; // full text");
            sb.AppendLine($"        using var engine_{node.Id} = new TesseractEngine(\"tessdata\", {CStr(lang)}, EngineMode.Default);");
            sb.AppendLine($"        engine_{node.Id}.SetVariable(\"tessedit_pageseg_mode\", \"{psm}\");");
            sb.AppendLine($"        var imgBytes_{node.Id} = {input1}.ToBytes(\".png\");");
            sb.AppendLine($"        using var pix_{node.Id} = Pix.LoadFromMemory(imgBytes_{node.Id});");
            sb.AppendLine($"        using var page_{node.Id} = engine_{node.Id}.Process(pix_{node.Id});");
            if (resultVars.Count > 5) sb.AppendLine($"        {resultVars[5]} = page_{node.Id}.GetText().Trim();");
            sb.AppendLine($"        using var iter_{node.Id} = page_{node.Id}.GetIterator();");
            sb.AppendLine($"        iter_{node.Id}.Begin();");
            sb.AppendLine($"        do {{");
            sb.AppendLine($"            var text = iter_{node.Id}.GetText(PageIteratorLevel.Word);");
            sb.AppendLine($"            var conf = iter_{node.Id}.GetConfidence(PageIteratorLevel.Word) / 100.0;");
            sb.AppendLine($"            if (text != null && conf >= {confidence} && text.Trim().Length > 0)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                iter_{node.Id}.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds);");
            sb.AppendLine($"                var r = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height);");
            if (resultVars.Count > 1) sb.AppendLine($"                {resultVars[1]}.Add(text.Trim());");
            if (resultVars.Count > 2) sb.AppendLine($"                {resultVars[2]}.Add(r);");
            if (resultVars.Count > 3) sb.AppendLine($"                {resultVars[3]}.Add(conf);");
            if (resultVars.Count > 4) sb.AppendLine($"                {resultVars[4]}++;");
            sb.AppendLine($"                Cv2.Rectangle({primaryResult}, r, new Scalar(0, 255, 0), 2);");
            sb.AppendLine($"                Cv2.PutText({primaryResult}, text.Trim(), new Point(r.X, r.Y - 5), HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 255, 0), 1);");
            sb.AppendLine($"            }}");
            sb.AppendLine($"        }} while (iter_{node.Id}.Next(PageIteratorLevel.Word));");
        }

        // =====================================================================
        // AI VISION (LLM/VLM via HttpClient)
        // =====================================================================
        else if (MatchName(name, "OpenAI Vision", "GPT Vision"))
        {
            var apiKey = GetPropStr(props, "ApiKey");
            var model = GetPropStr(props, "Model");
            if (string.IsNullOrEmpty(model)) model = "gpt-4o-mini";
            var maxTokens = GetPropNum(props, "MaxTokens", 1024);
            var temperature = GetPropDouble(props, "Temperature", 0.7);
            var systemPrompt = GetPropStr(props, "SystemPrompt");
            if (string.IsNullOrEmpty(systemPrompt)) systemPrompt = "You are a helpful vision assistant.";
            sb.AppendLine($"        // OpenAI Vision API call");
            sb.AppendLine($"        string {primaryResult};");
            var promptInput = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : CStr("Describe this image.");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var imgBytes = {input1}.ToBytes(\".png\");");
            sb.AppendLine($"            var b64 = Convert.ToBase64String(imgBytes);");
            sb.AppendLine($"            var apiKey = {CStr(apiKey)};");
            sb.AppendLine($"            if (string.IsNullOrEmpty(apiKey)) apiKey = Environment.GetEnvironmentVariable(\"OPENAI_API_KEY\") ?? \"\";");
            sb.AppendLine($"            using var http = new HttpClient();");
            sb.AppendLine($"            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(\"Bearer\", apiKey);");
            sb.AppendLine($"            var body = JsonSerializer.Serialize(new {{ model = {CStr(model)}, max_tokens = {maxTokens}, temperature = {temperature},");
            sb.AppendLine($"                messages = new object[] {{ new {{ role = \"system\", content = {CStr(systemPrompt)} }},");
            sb.AppendLine($"                    new {{ role = \"user\", content = new object[] {{");
            sb.AppendLine($"                        new {{ type = \"text\", text = (string){promptInput} }},");
            sb.AppendLine($"                        new {{ type = \"image_url\", image_url = new {{ url = $\"data:image/png;base64,{{b64}}\" }} }} }} }} }} }});");
            sb.AppendLine($"            var resp = http.PostAsync(\"https://api.openai.com/v1/chat/completions\",");
            sb.AppendLine($"                new StringContent(body, System.Text.Encoding.UTF8, \"application/json\")).Result;");
            sb.AppendLine($"            var json = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);");
            sb.AppendLine($"            {primaryResult} = json.RootElement.GetProperty(\"choices\")[0].GetProperty(\"message\").GetProperty(\"content\").GetString() ?? \"Error\";");
            sb.AppendLine($"        }}");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"        var {resultVars[1]} = {input1}.Clone();");
                matVars.Add(resultVars[1]);
                sb.AppendLine($"        Cv2.PutText({resultVars[1]}, {primaryResult}.Length > 80 ? {primaryResult}[..80] : {primaryResult}, new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 1);");
            }
        }
        else if (MatchName(name, "Gemini Vision"))
        {
            var apiKey = GetPropStr(props, "ApiKey");
            var model = GetPropStr(props, "Model");
            if (string.IsNullOrEmpty(model)) model = "gemini-2.0-flash";
            var maxTokens = GetPropNum(props, "MaxTokens", 1024);
            var temperature = GetPropDouble(props, "Temperature", 0.7);
            var systemPrompt = GetPropStr(props, "SystemPrompt");
            if (string.IsNullOrEmpty(systemPrompt)) systemPrompt = "You are a helpful vision assistant.";
            sb.AppendLine($"        // Google Gemini Vision API call");
            sb.AppendLine($"        string {primaryResult};");
            var promptInput2 = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : CStr("Describe this image.");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var imgBytes = {input1}.ToBytes(\".png\");");
            sb.AppendLine($"            var b64 = Convert.ToBase64String(imgBytes);");
            sb.AppendLine($"            var apiKey = {CStr(apiKey)};");
            sb.AppendLine($"            if (string.IsNullOrEmpty(apiKey)) apiKey = Environment.GetEnvironmentVariable(\"GEMINI_API_KEY\") ?? \"\";");
            sb.AppendLine($"            using var http = new HttpClient();");
            sb.AppendLine($"            var body = JsonSerializer.Serialize(new {{");
            sb.AppendLine($"                system_instruction = new {{ parts = new[] {{ new {{ text = {CStr(systemPrompt)} }} }} }},");
            sb.AppendLine($"                generationConfig = new {{ maxOutputTokens = {maxTokens}, temperature = {temperature} }},");
            sb.AppendLine($"                contents = new[] {{ new {{ parts = new object[] {{");
            sb.AppendLine($"                    new {{ text = (string){promptInput2} }},");
            sb.AppendLine($"                    new {{ inline_data = new {{ mime_type = \"image/png\", data = b64 }} }} }} }} }} }});");
            sb.AppendLine($"            var resp = http.PostAsync($\"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={{apiKey}}\",");
            sb.AppendLine($"                new StringContent(body, System.Text.Encoding.UTF8, \"application/json\")).Result;");
            sb.AppendLine($"            var json = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);");
            sb.AppendLine($"            {primaryResult} = json.RootElement.GetProperty(\"candidates\")[0].GetProperty(\"content\").GetProperty(\"parts\")[0].GetProperty(\"text\").GetString() ?? \"Error\";");
            sb.AppendLine($"        }}");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"        var {resultVars[1]} = {input1}.Clone();");
                matVars.Add(resultVars[1]);
                sb.AppendLine($"        Cv2.PutText({resultVars[1]}, {primaryResult}.Length > 80 ? {primaryResult}[..80] : {primaryResult}, new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 1);");
            }
        }
        else if (MatchName(name, "Claude Vision"))
        {
            var apiKey = GetPropStr(props, "ApiKey");
            var model = GetPropStr(props, "Model");
            if (string.IsNullOrEmpty(model)) model = "claude-sonnet-4-20250514";
            var maxTokens = GetPropNum(props, "MaxTokens", 1024);
            var temperature = GetPropDouble(props, "Temperature", 0.7);
            var systemPrompt = GetPropStr(props, "SystemPrompt");
            if (string.IsNullOrEmpty(systemPrompt)) systemPrompt = "You are a helpful vision assistant.";
            sb.AppendLine($"        // Anthropic Claude Vision API call");
            sb.AppendLine($"        string {primaryResult};");
            var promptInput3 = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : CStr("Describe this image.");
            sb.AppendLine($"        {{");
            sb.AppendLine($"            var imgBytes = {input1}.ToBytes(\".png\");");
            sb.AppendLine($"            var b64 = Convert.ToBase64String(imgBytes);");
            sb.AppendLine($"            var apiKey = {CStr(apiKey)};");
            sb.AppendLine($"            if (string.IsNullOrEmpty(apiKey)) apiKey = Environment.GetEnvironmentVariable(\"ANTHROPIC_API_KEY\") ?? \"\";");
            sb.AppendLine($"            using var http = new HttpClient();");
            sb.AppendLine($"            http.DefaultRequestHeaders.Add(\"x-api-key\", apiKey);");
            sb.AppendLine($"            http.DefaultRequestHeaders.Add(\"anthropic-version\", \"2023-06-01\");");
            sb.AppendLine($"            var body = JsonSerializer.Serialize(new {{ model = {CStr(model)}, max_tokens = {maxTokens}, temperature = {temperature},");
            sb.AppendLine($"                system = {CStr(systemPrompt)},");
            sb.AppendLine($"                messages = new[] {{ new {{ role = \"user\", content = new object[] {{");
            sb.AppendLine($"                    new {{ type = \"image\", source = new {{ type = \"base64\", media_type = \"image/png\", data = b64 }} }},");
            sb.AppendLine($"                    new {{ type = \"text\", text = (string){promptInput3} }} }} }} }} }});");
            sb.AppendLine($"            var resp = http.PostAsync(\"https://api.anthropic.com/v1/messages\",");
            sb.AppendLine($"                new StringContent(body, System.Text.Encoding.UTF8, \"application/json\")).Result;");
            sb.AppendLine($"            var json = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);");
            sb.AppendLine($"            {primaryResult} = json.RootElement.GetProperty(\"content\")[0].GetProperty(\"text\").GetString() ?? \"Error\";");
            sb.AppendLine($"        }}");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"        var {resultVars[1]} = {input1}.Clone();");
                matVars.Add(resultVars[1]);
                sb.AppendLine($"        Cv2.PutText({resultVars[1]}, {primaryResult}.Length > 80 ? {primaryResult}[..80] : {primaryResult}, new Point(10, 30), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 1);");
            }
        }

        // =====================================================================
        // UNKNOWN / FALLBACK
        // =====================================================================
        else
        {
            sb.AppendLine($"        // TODO: Unknown node type '{node.Name}' (category: {node.Category})");
            sb.AppendLine($"        // Inputs: {string.Join(", ", node.Inputs.Select(i => i.Name))}");
            sb.AppendLine($"        // Outputs: {string.Join(", ", node.Outputs.Select(o => o.Name))}");
            sb.AppendLine($"        // Properties: {string.Join(", ", node.Properties.Select(p => $"{p.Name}={p.Value}"))}");
            sb.AppendLine($"        object? {primaryResult} = null; // Placeholder");
        }
    }

    // =========================================================================
    // Helper methods
    // =========================================================================

    private static string SanitizeName(string name)
    {
        return new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).ToLowerInvariant();
    }

    private static bool MatchName(string nodeName, params string[] candidates)
    {
        var normalized = nodeName.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
        foreach (var c in candidates)
        {
            var cn = c.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (normalized == cn || normalized.Contains(cn) || cn.Contains(normalized))
                return true;
        }
        return false;
    }

    private static string GetInput(Dictionary<string, string> inputVarMap, INode node, int index)
    {
        if (index < node.Inputs.Count)
        {
            var inp = node.Inputs[index];
            if (inputVarMap.TryGetValue(inp.Name, out var varName))
                return varName;
        }
        return $"null /* unconnected input {index} */";
    }

    private static string GetPropStr(Dictionary<string, object?> props, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (props.TryGetValue(key, out var val) && val != null)
            {
                var s = val.ToString() ?? "";
                if (!string.IsNullOrEmpty(s))
                    return s;
            }
        }
        return "";
    }

    private static int GetPropNum(Dictionary<string, object?> props, params object[] keysAndDefault)
    {
        int defaultVal = 0;
        var keys = new List<string>();
        foreach (var item in keysAndDefault)
        {
            if (item is string s)
                keys.Add(s);
            else if (item is int i)
                defaultVal = i;
        }

        foreach (var key in keys)
        {
            if (props.TryGetValue(key, out var val) && val != null)
            {
                if (val is int iv) return iv;
                if (val is float fv) return (int)fv;
                if (val is double dv) return (int)dv;
                if (int.TryParse(val.ToString(), out var parsed)) return parsed;
            }
        }
        return defaultVal;
    }

    private static double GetPropDouble(Dictionary<string, object?> props, params object[] keysAndDefault)
    {
        double defaultVal = 0;
        var keys = new List<string>();
        foreach (var item in keysAndDefault)
        {
            if (item is string s)
                keys.Add(s);
            else if (item is double d)
                defaultVal = d;
            else if (item is int i)
                defaultVal = i;
            else if (item is float f)
                defaultVal = f;
        }

        foreach (var key in keys)
        {
            if (props.TryGetValue(key, out var val) && val != null)
            {
                if (val is double dv) return dv;
                if (val is float fv) return fv;
                if (val is int iv) return iv;
                if (double.TryParse(val.ToString(), out var parsed)) return parsed;
            }
        }
        return defaultVal;
    }

    private static bool GetPropBool(Dictionary<string, object?> props, params object[] keysAndDefault)
    {
        bool defaultVal = false;
        var keys = new List<string>();
        foreach (var item in keysAndDefault)
        {
            if (item is string s)
                keys.Add(s);
            else if (item is bool b)
                defaultVal = b;
        }

        foreach (var key in keys)
        {
            if (props.TryGetValue(key, out var val) && val != null)
            {
                if (val is bool bv) return bv;
                if (bool.TryParse(val.ToString(), out var parsed)) return parsed;
            }
        }
        return defaultVal;
    }

    private static string CStr(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string CBool(bool value) => value ? "true" : "false";
}
