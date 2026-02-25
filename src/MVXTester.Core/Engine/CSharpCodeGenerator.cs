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

        foreach (var node in sorted)
        {
            var name = node.Name;
            if (name.Contains("TCP") || name.Contains("Socket"))
                needsSockets = true;
            if (name.Contains("Serial"))
                needsSerial = true;
            if (name.Contains("CSV"))
                needsCsv = true;
        }

        // --- Using statements ---
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
