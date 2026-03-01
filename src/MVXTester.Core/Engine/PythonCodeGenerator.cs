using System.Text;
using MVXTester.Core.Models;

namespace MVXTester.Core.Engine;

/// <summary>
/// Generates executable Python OpenCV (cv2) code from a node graph.
/// </summary>
public static class PythonCodeGenerator
{
    /// <summary>
    /// Generates a complete Python script from the given node graph.
    /// Performs topological sort, maps each node to its cv2 equivalent,
    /// and wires inputs/outputs via named variables.
    /// </summary>
    public static string Generate(NodeGraph graph)
    {
        var sb = new StringBuilder();
        var sorted = GraphExecutor.TopologicalSort(graph.Nodes, graph.Connections);

        // Build a lookup: node -> variable name for each output port
        var outputVars = new Dictionary<(string NodeId, string PortName), string>();

        // --- Imports ---
        sb.AppendLine("import cv2");
        sb.AppendLine("import numpy as np");
        sb.AppendLine("import sys");
        sb.AppendLine("import os");
        sb.AppendLine();

        // Scan for special imports
        bool needsSocket = false;
        bool needsSerial = false;
        bool needsCsv = false;
        bool needsJson = false;
        bool needsMediaPipe = false;
        bool needsYolo = false;
        bool needsTesseract = false;
        bool needsPaddleOCR = false;
        bool needsRequests = false;
        bool needsBase64 = false;

        foreach (var node in sorted)
        {
            var name = node.Name;
            if (name.Contains("TCP") || name.Contains("Socket"))
                needsSocket = true;
            if (name.Contains("Serial"))
                needsSerial = true;
            if (name.Contains("CSV"))
                needsCsv = true;
            if (name.Contains("JSON"))
                needsJson = true;
            if (name.Contains("MP ") || node.Category == "MediaPipe")
                needsMediaPipe = true;
            if (name.Contains("YOLO") || node.Category == "YOLO")
                needsYolo = true;
            if (name.Contains("Tesseract"))
                needsTesseract = true;
            if (name.Contains("PaddleOCR"))
                needsPaddleOCR = true;
            if (name.Contains("OpenAI") || name.Contains("Gemini") || name.Contains("Claude"))
            {
                needsRequests = true;
                needsBase64 = true;
                needsJson = true;
            }
        }

        if (needsSocket)
            sb.AppendLine("import socket");
        if (needsSerial)
            sb.AppendLine("import serial");
        if (needsCsv)
            sb.AppendLine("import csv");
        if (needsJson)
            sb.AppendLine("import json");
        if (needsRequests)
            sb.AppendLine("import requests");
        if (needsBase64)
            sb.AppendLine("import base64");
        if (needsMediaPipe)
            sb.AppendLine("import mediapipe as mp");
        if (needsYolo)
            sb.AppendLine("from ultralytics import YOLO");
        if (needsTesseract)
            sb.AppendLine("import pytesseract");
        if (needsPaddleOCR)
            sb.AppendLine("from paddleocr import PaddleOCR");

        // Print pip install hints as comment
        var pipPackages = new List<string>();
        pipPackages.Add("opencv-python");
        pipPackages.Add("numpy");
        if (needsSocket) { } // built-in
        if (needsSerial) pipPackages.Add("pyserial");
        if (needsMediaPipe) pipPackages.Add("mediapipe");
        if (needsYolo) pipPackages.Add("ultralytics");
        if (needsTesseract) pipPackages.Add("pytesseract");
        if (needsPaddleOCR) pipPackages.Add("paddleocr");
        if (needsRequests) pipPackages.Add("requests");

        sb.AppendLine();
        sb.AppendLine($"# pip install {string.Join(" ", pipPackages)}");

        if (needsSocket || needsSerial || needsCsv || needsJson || needsMediaPipe || needsYolo || needsTesseract || needsPaddleOCR || needsRequests || needsBase64)
            sb.AppendLine();

        sb.AppendLine();
        sb.AppendLine("def main():");

        if (sorted.Count == 0)
        {
            sb.AppendLine("    pass");
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("if __name__ == \"__main__\":");
            sb.AppendLine("    main()");
            return sb.ToString();
        }

        // --- Generate code for each node ---
        foreach (var node in sorted)
        {
            sb.AppendLine();
            sb.AppendLine($"    # --- {node.Name} (id: {node.Id}) ---");

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

            // Map node by name to Python code
            GenerateNodeCode(sb, node, primaryResult, resultVars, inputVarMap, props, input1, input2);
        }

        sb.AppendLine();
        sb.AppendLine("    cv2.destroyAllWindows()");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("if __name__ == \"__main__\":");
        sb.AppendLine("    main()");

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
        string input2)
    {
        var name = node.Name;

        // =====================================================================
        // INPUT / OUTPUT
        // =====================================================================
        if (MatchName(name, "Read Image", "Load Image", "imread"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"    {primaryResult} = cv2.imread({PyStr(path)})");
        }
        else if (MatchName(name, "Write Image", "Save Image", "imwrite"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"    cv2.imwrite({PyStr(path)}, {input1})");
        }
        else if (MatchName(name, "Video Capture", "Camera", "VideoCapture"))
        {
            var src = GetPropStr(props, "Source", "DeviceIndex", "Path");
            var sourceVal = int.TryParse(src, out var idx) ? idx.ToString() : PyStr(src);
            sb.AppendLine($"    {primaryResult} = cv2.VideoCapture({sourceVal})");
        }
        else if (MatchName(name, "Read Frame", "Video Read"))
        {
            sb.AppendLine($"    ret_{node.Id}, {primaryResult} = {input1}.read()");
        }
        else if (MatchName(name, "Display", "Show Image", "imshow", "Image Display"))
        {
            var winName = GetPropStr(props, "WindowName", "Title");
            if (string.IsNullOrEmpty(winName)) winName = $"Display_{node.Id}";
            sb.AppendLine($"    cv2.imshow({PyStr(winName)}, {input1})");
            sb.AppendLine($"    cv2.waitKey(1)");
        }
        else if (MatchName(name, "Video Writer", "VideoWriter"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            var fourcc = GetPropStr(props, "FourCC", "Codec");
            if (string.IsNullOrEmpty(fourcc)) fourcc = "XVID";
            var fps = GetPropNum(props, "FPS", "FrameRate", 30);
            sb.AppendLine($"    fourcc_{node.Id} = cv2.VideoWriter_fourcc(*{PyStr(fourcc)})");
            sb.AppendLine($"    {primaryResult} = cv2.VideoWriter({PyStr(path)}, fourcc_{node.Id}, {fps}, (640, 480))");
        }
        else if (MatchName(name, "Wait Key", "WaitKey"))
        {
            var delay = GetPropNum(props, "Delay", "Milliseconds", 0);
            sb.AppendLine($"    {primaryResult} = cv2.waitKey({delay})");
        }

        // =====================================================================
        // COLOR
        // =====================================================================
        else if (MatchName(name, "Convert Color", "CvtColor", "Color Convert", "Color Space"))
        {
            var code = GetPropStr(props, "ConversionCode", "Code", "ColorConversion");
            if (string.IsNullOrEmpty(code)) code = "cv2.COLOR_BGR2GRAY";
            else if (!code.StartsWith("cv2.")) code = $"cv2.{code}";
            sb.AppendLine($"    {primaryResult} = cv2.cvtColor({input1}, {code})");
        }
        else if (MatchName(name, "In Range", "InRange", "Color Range"))
        {
            var lower = GetPropStr(props, "Lower", "LowerBound", "LowerB");
            var upper = GetPropStr(props, "Upper", "UpperBound", "UpperB");
            if (string.IsNullOrEmpty(lower)) lower = "np.array([0, 0, 0])";
            if (string.IsNullOrEmpty(upper)) upper = "np.array([255, 255, 255])";
            sb.AppendLine($"    {primaryResult} = cv2.inRange({input1}, {lower}, {upper})");
        }
        else if (MatchName(name, "Split Channels", "Split", "Channel Split"))
        {
            if (resultVars.Count >= 3)
                sb.AppendLine($"    {resultVars[0]}, {resultVars[1]}, {resultVars[2]} = cv2.split({input1})");
            else
                sb.AppendLine($"    {primaryResult} = cv2.split({input1})");
        }
        else if (MatchName(name, "Merge Channels", "Merge", "Channel Merge"))
        {
            var channels = string.Join(", ", node.Inputs
                .Where(i => inputVarMap.ContainsKey(i.Name))
                .Select(i => inputVarMap[i.Name]));
            if (string.IsNullOrEmpty(channels)) channels = input1;
            sb.AppendLine($"    {primaryResult} = cv2.merge([{channels}])");
        }

        // =====================================================================
        // FILTER
        // =====================================================================
        else if (MatchName(name, "Gaussian Blur", "GaussianBlur"))
        {
            var kx = GetPropNum(props, "KernelWidth", "KernelX", "KernelSize", 5);
            var ky = GetPropNum(props, "KernelHeight", "KernelY", "KernelSize", 5);
            var sigmaX = GetPropDouble(props, "SigmaX", "Sigma", 0);
            sb.AppendLine($"    {primaryResult} = cv2.GaussianBlur({input1}, ({kx}, {ky}), {sigmaX})");
        }
        else if (MatchName(name, "Median Blur", "MedianBlur"))
        {
            var k = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"    {primaryResult} = cv2.medianBlur({input1}, {k})");
        }
        else if (MatchName(name, "Bilateral Filter", "BilateralFilter"))
        {
            var d = GetPropNum(props, "Diameter", "D", 9);
            var sigmaColor = GetPropDouble(props, "SigmaColor", 75);
            var sigmaSpace = GetPropDouble(props, "SigmaSpace", 75);
            sb.AppendLine($"    {primaryResult} = cv2.bilateralFilter({input1}, {d}, {sigmaColor}, {sigmaSpace})");
        }
        else if (MatchName(name, "Blur", "Average Blur", "Box Blur", "BoxFilter"))
        {
            var kx = GetPropNum(props, "KernelWidth", "KernelX", "KernelSize", 5);
            var ky = GetPropNum(props, "KernelHeight", "KernelY", "KernelSize", 5);
            sb.AppendLine($"    {primaryResult} = cv2.blur({input1}, ({kx}, {ky}))");
        }
        else if (MatchName(name, "Filter2D", "Custom Filter", "Convolution"))
        {
            sb.AppendLine($"    # Define your custom kernel");
            sb.AppendLine($"    kernel_{node.Id} = np.array([[0, -1, 0], [-1, 5, -1], [0, -1, 0]], dtype=np.float32)");
            sb.AppendLine($"    {primaryResult} = cv2.filter2D({input1}, -1, kernel_{node.Id})");
        }
        else if (MatchName(name, "Sharpen"))
        {
            sb.AppendLine($"    blurred_{node.Id} = cv2.GaussianBlur({input1}, (0, 0), 3)");
            var amount = GetPropDouble(props, "Amount", "Strength", 1.5);
            sb.AppendLine($"    {primaryResult} = cv2.addWeighted({input1}, {amount}, blurred_{node.Id}, {1.0 - amount}, 0)");
        }

        // =====================================================================
        // EDGE DETECTION
        // =====================================================================
        else if (MatchName(name, "Canny", "Canny Edge"))
        {
            var t1 = GetPropDouble(props, "Threshold1", "LowerThreshold", "MinThreshold", 100);
            var t2 = GetPropDouble(props, "Threshold2", "UpperThreshold", "MaxThreshold", 200);
            sb.AppendLine($"    {primaryResult} = cv2.Canny({input1}, {t1}, {t2})");
        }
        else if (MatchName(name, "Sobel"))
        {
            var dx = GetPropNum(props, "DX", "Dx", "OrderX", 1);
            var dy = GetPropNum(props, "DY", "Dy", "OrderY", 0);
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 3);
            sb.AppendLine($"    {primaryResult} = cv2.Sobel({input1}, cv2.CV_64F, {dx}, {dy}, ksize={ksize})");
        }
        else if (MatchName(name, "Laplacian"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 3);
            sb.AppendLine($"    {primaryResult} = cv2.Laplacian({input1}, cv2.CV_64F, ksize={ksize})");
        }
        else if (MatchName(name, "Scharr"))
        {
            var dx = GetPropNum(props, "DX", "Dx", "OrderX", 1);
            var dy = GetPropNum(props, "DY", "Dy", "OrderY", 0);
            sb.AppendLine($"    {primaryResult} = cv2.Scharr({input1}, cv2.CV_64F, {dx}, {dy})");
        }

        // =====================================================================
        // MORPHOLOGY
        // =====================================================================
        else if (MatchName(name, "Erode"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            var iterations = GetPropNum(props, "Iterations", 1);
            sb.AppendLine($"    kernel_{node.Id} = cv2.getStructuringElement(cv2.MORPH_RECT, ({ksize}, {ksize}))");
            sb.AppendLine($"    {primaryResult} = cv2.erode({input1}, kernel_{node.Id}, iterations={iterations})");
        }
        else if (MatchName(name, "Dilate"))
        {
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            var iterations = GetPropNum(props, "Iterations", 1);
            sb.AppendLine($"    kernel_{node.Id} = cv2.getStructuringElement(cv2.MORPH_RECT, ({ksize}, {ksize}))");
            sb.AppendLine($"    {primaryResult} = cv2.dilate({input1}, kernel_{node.Id}, iterations={iterations})");
        }
        else if (MatchName(name, "Morphology", "Morphology Ex", "MorphologyEx"))
        {
            var op = GetPropStr(props, "Operation", "MorphOp", "Op");
            if (string.IsNullOrEmpty(op)) op = "cv2.MORPH_OPEN";
            else if (!op.StartsWith("cv2.")) op = $"cv2.{op}";
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"    kernel_{node.Id} = cv2.getStructuringElement(cv2.MORPH_RECT, ({ksize}, {ksize}))");
            sb.AppendLine($"    {primaryResult} = cv2.morphologyEx({input1}, {op}, kernel_{node.Id})");
        }
        else if (MatchName(name, "Structuring Element", "GetStructuringElement"))
        {
            var shape = GetPropStr(props, "Shape", "ElementShape");
            if (string.IsNullOrEmpty(shape)) shape = "cv2.MORPH_RECT";
            else if (!shape.StartsWith("cv2.")) shape = $"cv2.{shape}";
            var ksize = GetPropNum(props, "KernelSize", "Ksize", 5);
            sb.AppendLine($"    {primaryResult} = cv2.getStructuringElement({shape}, ({ksize}, {ksize}))");
        }

        // =====================================================================
        // THRESHOLD
        // =====================================================================
        else if (MatchName(name, "Threshold"))
        {
            var thresh = GetPropDouble(props, "ThresholdValue", "Thresh", "Value", 127);
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            var thType = GetPropStr(props, "ThresholdType", "Type");
            if (string.IsNullOrEmpty(thType)) thType = "cv2.THRESH_BINARY";
            else if (!thType.StartsWith("cv2.")) thType = $"cv2.{thType}";
            sb.AppendLine($"    _, {primaryResult} = cv2.threshold({input1}, {thresh}, {maxVal}, {thType})");
        }
        else if (MatchName(name, "Adaptive Threshold", "AdaptiveThreshold"))
        {
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            var method = GetPropStr(props, "AdaptiveMethod", "Method");
            if (string.IsNullOrEmpty(method)) method = "cv2.ADAPTIVE_THRESH_GAUSSIAN_C";
            else if (!method.StartsWith("cv2.")) method = $"cv2.{method}";
            var thType = GetPropStr(props, "ThresholdType", "Type");
            if (string.IsNullOrEmpty(thType)) thType = "cv2.THRESH_BINARY";
            else if (!thType.StartsWith("cv2.")) thType = $"cv2.{thType}";
            var blockSize = GetPropNum(props, "BlockSize", 11);
            var c = GetPropDouble(props, "C", "Constant", 2);
            sb.AppendLine($"    {primaryResult} = cv2.adaptiveThreshold({input1}, {maxVal}, {method}, {thType}, {blockSize}, {c})");
        }
        else if (MatchName(name, "OTSU", "Otsu Threshold", "Otsu"))
        {
            var maxVal = GetPropDouble(props, "MaxValue", "MaxVal", 255);
            sb.AppendLine($"    _, {primaryResult} = cv2.threshold({input1}, 0, {maxVal}, cv2.THRESH_BINARY + cv2.THRESH_OTSU)");
        }

        // =====================================================================
        // CONTOUR
        // =====================================================================
        else if (MatchName(name, "Find Contours", "FindContours"))
        {
            var mode = GetPropStr(props, "RetrievalMode", "Mode");
            if (string.IsNullOrEmpty(mode)) mode = "cv2.RETR_EXTERNAL";
            else if (!mode.StartsWith("cv2.")) mode = $"cv2.{mode}";
            var approx = GetPropStr(props, "ApproximationMethod", "Method");
            if (string.IsNullOrEmpty(approx)) approx = "cv2.CHAIN_APPROX_SIMPLE";
            else if (!approx.StartsWith("cv2.")) approx = $"cv2.{approx}";
            sb.AppendLine($"    {primaryResult}, _ = cv2.findContours({input1}, {mode}, {approx})");
        }
        else if (MatchName(name, "Draw Contours", "DrawContours"))
        {
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            var idx = GetPropNum(props, "ContourIndex", "Index", -1);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.drawContours({primaryResult}, {input2}, {idx}, {color}, {thickness})");
        }
        else if (MatchName(name, "Contour Area", "ContourArea"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.contourArea({input1})");
        }
        else if (MatchName(name, "Approx Poly", "ApproxPolyDP", "Approximate Polygon"))
        {
            var epsilon = GetPropDouble(props, "Epsilon", "Accuracy", 0.02);
            var closed = GetPropBool(props, "Closed", true);
            sb.AppendLine($"    epsilon_{node.Id} = {epsilon} * cv2.arcLength({input1}, {PyBool(closed)})");
            sb.AppendLine($"    {primaryResult} = cv2.approxPolyDP({input1}, epsilon_{node.Id}, {PyBool(closed)})");
        }
        else if (MatchName(name, "Convex Hull", "ConvexHull"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.convexHull({input1})");
        }
        else if (MatchName(name, "Bounding Rect", "BoundingRect", "Bounding Rectangle"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.boundingRect({input1})");
        }
        else if (MatchName(name, "Min Enclosing Circle", "MinEnclosingCircle"))
        {
            if (resultVars.Count >= 2)
                sb.AppendLine($"    {resultVars[0]}, {resultVars[1]} = cv2.minEnclosingCircle({input1})");
            else
                sb.AppendLine($"    {primaryResult} = cv2.minEnclosingCircle({input1})");
        }

        // =====================================================================
        // FEATURE DETECTION
        // =====================================================================
        else if (MatchName(name, "ORB", "ORB Detector"))
        {
            var nFeatures = GetPropNum(props, "NFeatures", "MaxFeatures", 500);
            sb.AppendLine($"    orb_{node.Id} = cv2.ORB_create(nfeatures={nFeatures})");
            if (resultVars.Count >= 2)
                sb.AppendLine($"    {resultVars[0]}, {resultVars[1]} = orb_{node.Id}.detectAndCompute({input1}, None)");
            else
                sb.AppendLine($"    {primaryResult}, desc_{node.Id} = orb_{node.Id}.detectAndCompute({input1}, None)");
        }
        else if (MatchName(name, "SIFT", "SIFT Detector"))
        {
            var nFeatures = GetPropNum(props, "NFeatures", "MaxFeatures", 0);
            sb.AppendLine($"    sift_{node.Id} = cv2.SIFT_create(nfeatures={nFeatures})");
            if (resultVars.Count >= 2)
                sb.AppendLine($"    {resultVars[0]}, {resultVars[1]} = sift_{node.Id}.detectAndCompute({input1}, None)");
            else
                sb.AppendLine($"    {primaryResult}, desc_{node.Id} = sift_{node.Id}.detectAndCompute({input1}, None)");
        }
        else if (MatchName(name, "FAST", "FAST Detector"))
        {
            var threshold = GetPropNum(props, "Threshold", 10);
            sb.AppendLine($"    fast_{node.Id} = cv2.FastFeatureDetector_create(threshold={threshold})");
            sb.AppendLine($"    {primaryResult} = fast_{node.Id}.detect({input1}, None)");
        }
        else if (MatchName(name, "Harris Corner", "CornerHarris", "Harris"))
        {
            var blockSize = GetPropNum(props, "BlockSize", 2);
            var ksize = GetPropNum(props, "KernelSize", "Ksize", "ApertureSize", 3);
            var k = GetPropDouble(props, "K", "HarrisK", 0.04);
            sb.AppendLine($"    {primaryResult} = cv2.cornerHarris({input1}, {blockSize}, {ksize}, {k})");
        }
        else if (MatchName(name, "Good Features", "GoodFeaturesToTrack", "Shi-Tomasi"))
        {
            var maxCorners = GetPropNum(props, "MaxCorners", 25);
            var quality = GetPropDouble(props, "QualityLevel", "Quality", 0.01);
            var minDist = GetPropDouble(props, "MinDistance", 10);
            sb.AppendLine($"    {primaryResult} = cv2.goodFeaturesToTrack({input1}, {maxCorners}, {quality}, {minDist})");
        }
        else if (MatchName(name, "BF Matcher", "BFMatcher", "Brute Force Matcher"))
        {
            var normType = GetPropStr(props, "NormType", "Norm");
            if (string.IsNullOrEmpty(normType)) normType = "cv2.NORM_HAMMING";
            else if (!normType.StartsWith("cv2.")) normType = $"cv2.{normType}";
            sb.AppendLine($"    bf_{node.Id} = cv2.BFMatcher({normType}, crossCheck=True)");
            sb.AppendLine($"    {primaryResult} = bf_{node.Id}.match({input1}, {input2})");
        }

        // =====================================================================
        // DRAWING
        // =====================================================================
        else if (MatchName(name, "Draw Line", "Line"))
        {
            var pt1 = GetPropStr(props, "Point1", "Start", "Pt1");
            var pt2 = GetPropStr(props, "Point2", "End", "Pt2");
            if (string.IsNullOrEmpty(pt1)) pt1 = "(0, 0)";
            if (string.IsNullOrEmpty(pt2)) pt2 = "(100, 100)";
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.line({primaryResult}, {pt1}, {pt2}, {color}, {thickness})");
        }
        else if (MatchName(name, "Draw Rectangle", "Rectangle"))
        {
            var pt1 = GetPropStr(props, "Point1", "TopLeft", "Pt1");
            var pt2 = GetPropStr(props, "Point2", "BottomRight", "Pt2");
            if (string.IsNullOrEmpty(pt1)) pt1 = "(0, 0)";
            if (string.IsNullOrEmpty(pt2)) pt2 = "(100, 100)";
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.rectangle({primaryResult}, {pt1}, {pt2}, {color}, {thickness})");
        }
        else if (MatchName(name, "Draw Circle", "Circle"))
        {
            var center = GetPropStr(props, "Center");
            if (string.IsNullOrEmpty(center)) center = "(50, 50)";
            var radius = GetPropNum(props, "Radius", 25);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.circle({primaryResult}, {center}, {radius}, {color}, {thickness})");
        }
        else if (MatchName(name, "Draw Ellipse", "Ellipse"))
        {
            var center = GetPropStr(props, "Center");
            if (string.IsNullOrEmpty(center)) center = "(50, 50)";
            var axes = GetPropStr(props, "Axes", "Size");
            if (string.IsNullOrEmpty(axes)) axes = "(25, 15)";
            var angle = GetPropDouble(props, "Angle", 0);
            var startAngle = GetPropDouble(props, "StartAngle", 0);
            var endAngle = GetPropDouble(props, "EndAngle", 360);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.ellipse({primaryResult}, {center}, {axes}, {angle}, {startAngle}, {endAngle}, {color}, {thickness})");
        }
        else if (MatchName(name, "Put Text", "PutText", "Draw Text", "Text"))
        {
            var text = GetPropStr(props, "Text", "Content");
            if (string.IsNullOrEmpty(text)) text = "Hello";
            var org = GetPropStr(props, "Origin", "Position", "Org");
            if (string.IsNullOrEmpty(org)) org = "(10, 30)";
            var font = GetPropStr(props, "Font", "FontFace");
            if (string.IsNullOrEmpty(font)) font = "cv2.FONT_HERSHEY_SIMPLEX";
            else if (!font.StartsWith("cv2.")) font = $"cv2.{font}";
            var scale = GetPropDouble(props, "FontScale", "Scale", 1.0);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(255, 255, 255)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.putText({primaryResult}, {PyStr(text)}, {org}, {font}, {scale}, {color}, {thickness})");
        }
        else if (MatchName(name, "Polylines", "Draw Polylines"))
        {
            var isClosed = GetPropBool(props, "IsClosed", "Closed", true);
            var color = GetPropStr(props, "Color");
            if (string.IsNullOrEmpty(color)) color = "(0, 255, 0)";
            var thickness = GetPropNum(props, "Thickness", 2);
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.polylines({primaryResult}, [{input2}], {PyBool(isClosed)}, {color}, {thickness})");
        }

        // =====================================================================
        // TRANSFORM
        // =====================================================================
        else if (MatchName(name, "Resize"))
        {
            var width = GetPropNum(props, "Width", 640);
            var height = GetPropNum(props, "Height", 480);
            var interp = GetPropStr(props, "Interpolation");
            if (string.IsNullOrEmpty(interp)) interp = "cv2.INTER_LINEAR";
            else if (!interp.StartsWith("cv2.")) interp = $"cv2.{interp}";
            sb.AppendLine($"    {primaryResult} = cv2.resize({input1}, ({width}, {height}), interpolation={interp})");
        }
        else if (MatchName(name, "Rotate"))
        {
            var rotCode = GetPropStr(props, "RotateCode", "Code", "Rotation");
            if (string.IsNullOrEmpty(rotCode)) rotCode = "cv2.ROTATE_90_CLOCKWISE";
            else if (!rotCode.StartsWith("cv2.")) rotCode = $"cv2.{rotCode}";
            sb.AppendLine($"    {primaryResult} = cv2.rotate({input1}, {rotCode})");
        }
        else if (MatchName(name, "Crop", "ROI", "Region of Interest"))
        {
            var x = GetPropNum(props, "X", 0);
            var y = GetPropNum(props, "Y", 0);
            var w = GetPropNum(props, "Width", "W", 100);
            var h = GetPropNum(props, "Height", "H", 100);
            sb.AppendLine($"    {primaryResult} = {input1}[{y}:{y}+{h}, {x}:{x}+{w}]");
        }
        else if (MatchName(name, "Flip"))
        {
            var flipCode = GetPropNum(props, "FlipCode", "Code", 1);
            sb.AppendLine($"    {primaryResult} = cv2.flip({input1}, {flipCode})");
        }
        else if (MatchName(name, "Warp Affine", "WarpAffine", "Affine Transform"))
        {
            sb.AppendLine($"    rows_{node.Id}, cols_{node.Id} = {input1}.shape[:2]");
            sb.AppendLine($"    {primaryResult} = cv2.warpAffine({input1}, {input2}, (cols_{node.Id}, rows_{node.Id}))");
        }
        else if (MatchName(name, "Warp Perspective", "WarpPerspective", "Perspective Transform"))
        {
            sb.AppendLine($"    rows_{node.Id}, cols_{node.Id} = {input1}.shape[:2]");
            sb.AppendLine($"    {primaryResult} = cv2.warpPerspective({input1}, {input2}, (cols_{node.Id}, rows_{node.Id}))");
        }
        else if (MatchName(name, "Remap"))
        {
            var interp = GetPropStr(props, "Interpolation");
            if (string.IsNullOrEmpty(interp)) interp = "cv2.INTER_LINEAR";
            else if (!interp.StartsWith("cv2.")) interp = $"cv2.{interp}";
            sb.AppendLine($"    {primaryResult} = cv2.remap({input1}, {input2}, {GetInput(new Dictionary<string, string>(), node, 2)}, {interp})");
        }

        // =====================================================================
        // HISTOGRAM
        // =====================================================================
        else if (MatchName(name, "Calc Histogram", "CalcHist", "Calculate Histogram"))
        {
            var channels = GetPropStr(props, "Channels");
            if (string.IsNullOrEmpty(channels)) channels = "[0]";
            var histSize = GetPropStr(props, "HistSize", "Bins");
            if (string.IsNullOrEmpty(histSize)) histSize = "[256]";
            var ranges = GetPropStr(props, "Ranges");
            if (string.IsNullOrEmpty(ranges)) ranges = "[0, 256]";
            sb.AppendLine($"    {primaryResult} = cv2.calcHist([{input1}], {channels}, None, {histSize}, {ranges})");
        }
        else if (MatchName(name, "Equalize Histogram", "EqualizeHist", "Histogram Equalization"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.equalizeHist({input1})");
        }
        else if (MatchName(name, "CLAHE"))
        {
            var clipLimit = GetPropDouble(props, "ClipLimit", 2.0);
            var tileGridSizeX = GetPropNum(props, "TileGridSizeX", "TileGridSize", 8);
            var tileGridSizeY = GetPropNum(props, "TileGridSizeY", "TileGridSize", 8);
            sb.AppendLine($"    clahe_{node.Id} = cv2.createCLAHE(clipLimit={clipLimit}, tileGridSize=({tileGridSizeX}, {tileGridSizeY}))");
            sb.AppendLine($"    {primaryResult} = clahe_{node.Id}.apply({input1})");
        }

        // =====================================================================
        // ARITHMETIC
        // =====================================================================
        else if (MatchName(name, "Add"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.add({input1}, {input2})");
        }
        else if (MatchName(name, "Subtract"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.subtract({input1}, {input2})");
        }
        else if (MatchName(name, "Multiply"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.multiply({input1}, {input2})");
        }
        else if (MatchName(name, "Absolute Difference", "AbsDiff"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.absdiff({input1}, {input2})");
        }
        else if (MatchName(name, "Bitwise AND", "BitwiseAnd"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.bitwise_and({input1}, {input2})");
        }
        else if (MatchName(name, "Bitwise OR", "BitwiseOr"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.bitwise_or({input1}, {input2})");
        }
        else if (MatchName(name, "Bitwise XOR", "BitwiseXor"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.bitwise_xor({input1}, {input2})");
        }
        else if (MatchName(name, "Bitwise NOT", "BitwiseNot"))
        {
            sb.AppendLine($"    {primaryResult} = cv2.bitwise_not({input1})");
        }
        else if (MatchName(name, "Blend", "Weighted Add", "AddWeighted"))
        {
            var alpha = GetPropDouble(props, "Alpha", "Weight1", 0.5);
            var beta = GetPropDouble(props, "Beta", "Weight2", 0.5);
            var gamma = GetPropDouble(props, "Gamma", 0);
            sb.AppendLine($"    {primaryResult} = cv2.addWeighted({input1}, {alpha}, {input2}, {beta}, {gamma})");
        }

        // =====================================================================
        // DETECTION
        // =====================================================================
        else if (MatchName(name, "Hough Lines", "HoughLines"))
        {
            var rho = GetPropDouble(props, "Rho", 1);
            var theta = GetPropStr(props, "Theta");
            if (string.IsNullOrEmpty(theta)) theta = "np.pi / 180";
            var threshold = GetPropNum(props, "Threshold", 150);
            sb.AppendLine($"    {primaryResult} = cv2.HoughLines({input1}, {rho}, {theta}, {threshold})");
        }
        else if (MatchName(name, "Hough Lines P", "HoughLinesP", "Probabilistic Hough"))
        {
            var rho = GetPropDouble(props, "Rho", 1);
            var theta = GetPropStr(props, "Theta");
            if (string.IsNullOrEmpty(theta)) theta = "np.pi / 180";
            var threshold = GetPropNum(props, "Threshold", 50);
            var minLineLength = GetPropDouble(props, "MinLineLength", 50);
            var maxLineGap = GetPropDouble(props, "MaxLineGap", 10);
            sb.AppendLine($"    {primaryResult} = cv2.HoughLinesP({input1}, {rho}, {theta}, {threshold}, minLineLength={minLineLength}, maxLineGap={maxLineGap})");
        }
        else if (MatchName(name, "Hough Circles", "HoughCircles"))
        {
            var method = GetPropStr(props, "Method");
            if (string.IsNullOrEmpty(method)) method = "cv2.HOUGH_GRADIENT";
            else if (!method.StartsWith("cv2.")) method = $"cv2.{method}";
            var dp = GetPropDouble(props, "Dp", "DP", 1);
            var minDist = GetPropDouble(props, "MinDist", "MinDistance", 20);
            sb.AppendLine($"    {primaryResult} = cv2.HoughCircles({input1}, {method}, {dp}, {minDist})");
        }
        else if (MatchName(name, "Match Template", "MatchTemplate", "Template Match"))
        {
            var method = GetPropStr(props, "Method", "MatchMethod");
            if (string.IsNullOrEmpty(method)) method = "cv2.TM_CCOEFF_NORMED";
            else if (!method.StartsWith("cv2.")) method = $"cv2.{method}";
            sb.AppendLine($"    {primaryResult} = cv2.matchTemplate({input1}, {input2}, {method})");
        }
        else if (MatchName(name, "Cascade Classifier", "CascadeClassifier", "Haar Cascade"))
        {
            var cascadePath = GetPropStr(props, "CascadePath", "Path", "FilePath");
            if (string.IsNullOrEmpty(cascadePath))
                cascadePath = "cv2.data.haarcascades + 'haarcascade_frontalface_default.xml'";
            else
                cascadePath = PyStr(cascadePath);
            sb.AppendLine($"    cascade_{node.Id} = cv2.CascadeClassifier({cascadePath})");
            var scaleFactor = GetPropDouble(props, "ScaleFactor", 1.1);
            var minNeighbors = GetPropNum(props, "MinNeighbors", 5);
            sb.AppendLine($"    {primaryResult} = cascade_{node.Id}.detectMultiScale({input1}, scaleFactor={scaleFactor}, minNeighbors={minNeighbors})");
        }

        // =====================================================================
        // SEGMENTATION
        // =====================================================================
        else if (MatchName(name, "GrabCut"))
        {
            var iterCount = GetPropNum(props, "IterCount", "Iterations", 5);
            var mode = GetPropStr(props, "Mode");
            if (string.IsNullOrEmpty(mode)) mode = "cv2.GC_INIT_WITH_RECT";
            else if (!mode.StartsWith("cv2.")) mode = $"cv2.{mode}";
            sb.AppendLine($"    mask_{node.Id} = np.zeros({input1}.shape[:2], np.uint8)");
            sb.AppendLine($"    bgModel_{node.Id} = np.zeros((1, 65), np.float64)");
            sb.AppendLine($"    fgModel_{node.Id} = np.zeros((1, 65), np.float64)");
            sb.AppendLine($"    rect_{node.Id} = (50, 50, {input1}.shape[1]-100, {input1}.shape[0]-100)  # Adjust as needed");
            sb.AppendLine($"    cv2.grabCut({input1}, mask_{node.Id}, rect_{node.Id}, bgModel_{node.Id}, fgModel_{node.Id}, {iterCount}, {mode})");
            sb.AppendLine($"    mask2_{node.Id} = np.where((mask_{node.Id} == 2) | (mask_{node.Id} == 0), 0, 1).astype('uint8')");
            sb.AppendLine($"    {primaryResult} = {input1} * mask2_{node.Id}[:, :, np.newaxis]");
        }
        else if (MatchName(name, "Watershed"))
        {
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.watershed({primaryResult}, {input2})");
        }
        else if (MatchName(name, "Flood Fill", "FloodFill"))
        {
            var seedPoint = GetPropStr(props, "SeedPoint", "Seed");
            if (string.IsNullOrEmpty(seedPoint)) seedPoint = "(0, 0)";
            var newVal = GetPropStr(props, "NewValue", "NewVal");
            if (string.IsNullOrEmpty(newVal)) newVal = "(255, 255, 255)";
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            sb.AppendLine($"    cv2.floodFill({primaryResult}, None, {seedPoint}, {newVal})");
        }

        // =====================================================================
        // VALUE NODES
        // =====================================================================
        else if (MatchName(name, "Integer", "Int Value", "Integer Value"))
        {
            var val = GetPropNum(props, "Value", 0);
            sb.AppendLine($"    {primaryResult} = {val}");
        }
        else if (MatchName(name, "Float", "Float Value", "Double", "Double Value"))
        {
            var val = GetPropDouble(props, "Value", 0.0);
            sb.AppendLine($"    {primaryResult} = {val}");
        }
        else if (MatchName(name, "Boolean", "Bool Value", "Boolean Value"))
        {
            var val = GetPropBool(props, "Value", false);
            sb.AppendLine($"    {primaryResult} = {PyBool(val)}");
        }
        else if (MatchName(name, "String", "String Value", "Text Value"))
        {
            var val = GetPropStr(props, "Value");
            sb.AppendLine($"    {primaryResult} = {PyStr(val)}");
        }
        else if (MatchName(name, "Point", "Point Value"))
        {
            var x = GetPropNum(props, "X", 0);
            var y = GetPropNum(props, "Y", 0);
            sb.AppendLine($"    {primaryResult} = ({x}, {y})");
        }
        else if (MatchName(name, "Size", "Size Value"))
        {
            var w = GetPropNum(props, "Width", "W", 640);
            var h = GetPropNum(props, "Height", "H", 480);
            sb.AppendLine($"    {primaryResult} = ({w}, {h})");
        }
        else if (MatchName(name, "Scalar", "Scalar Value", "Color Value"))
        {
            var b = GetPropNum(props, "B", "Blue", 0);
            var g = GetPropNum(props, "G", "Green", 0);
            var r = GetPropNum(props, "R", "Red", 0);
            sb.AppendLine($"    {primaryResult} = ({b}, {g}, {r})");
        }

        // =====================================================================
        // CONTROL
        // =====================================================================
        else if (MatchName(name, "If", "If/Else", "Branch", "Condition"))
        {
            sb.AppendLine($"    if {input1}:");
            sb.AppendLine($"        {primaryResult} = {input2}  # True branch");
            if (node.Inputs.Count > 2)
            {
                var input3 = GetInput(new Dictionary<string, string>(), node, 2);
                sb.AppendLine($"    else:");
                sb.AppendLine($"        {primaryResult} = {input3}  # False branch");
            }
        }
        else if (MatchName(name, "For Loop", "Loop", "For"))
        {
            var count = GetPropNum(props, "Count", "Iterations", 10);
            sb.AppendLine($"    for i_{node.Id} in range({count}):");
            sb.AppendLine($"        {primaryResult} = i_{node.Id}  # Loop variable");
            sb.AppendLine($"        pass  # Add loop body here");
        }
        else if (MatchName(name, "Switch", "Select"))
        {
            sb.AppendLine($"    # Switch on {input1}");
            sb.AppendLine($"    {primaryResult} = {{");
            sb.AppendLine($"        # Add cases here: value: result");
            sb.AppendLine($"    }}.get({input1}, None)");
        }

        // =====================================================================
        // COMMUNICATION
        // =====================================================================
        else if (MatchName(name, "TCP Client", "Socket Client", "TCP Send"))
        {
            var host = GetPropStr(props, "Host", "Address");
            if (string.IsNullOrEmpty(host)) host = "127.0.0.1";
            var port = GetPropNum(props, "Port", 5000);
            sb.AppendLine($"    sock_{node.Id} = socket.socket(socket.AF_INET, socket.SOCK_STREAM)");
            sb.AppendLine($"    sock_{node.Id}.connect(({PyStr(host)}, {port}))");
            sb.AppendLine($"    sock_{node.Id}.sendall(str({input1}).encode())");
            sb.AppendLine($"    {primaryResult} = sock_{node.Id}.recv(4096).decode()");
            sb.AppendLine($"    sock_{node.Id}.close()");
        }
        else if (MatchName(name, "TCP Server", "Socket Server", "TCP Listen"))
        {
            var host = GetPropStr(props, "Host", "Address");
            if (string.IsNullOrEmpty(host)) host = "0.0.0.0";
            var port = GetPropNum(props, "Port", 5000);
            sb.AppendLine($"    server_{node.Id} = socket.socket(socket.AF_INET, socket.SOCK_STREAM)");
            sb.AppendLine($"    server_{node.Id}.bind(({PyStr(host)}, {port}))");
            sb.AppendLine($"    server_{node.Id}.listen(1)");
            sb.AppendLine($"    conn_{node.Id}, addr_{node.Id} = server_{node.Id}.accept()");
            sb.AppendLine($"    {primaryResult} = conn_{node.Id}.recv(4096).decode()");
        }
        else if (MatchName(name, "Serial", "Serial Port", "Serial Read", "Serial Write"))
        {
            var portName = GetPropStr(props, "PortName", "Port", "COM");
            if (string.IsNullOrEmpty(portName)) portName = "COM3";
            var baudRate = GetPropNum(props, "BaudRate", "Baud", 9600);
            sb.AppendLine($"    ser_{node.Id} = serial.Serial({PyStr(portName)}, {baudRate}, timeout=1)");
            if (MatchName(name, "Serial Write"))
            {
                sb.AppendLine($"    ser_{node.Id}.write(str({input1}).encode())");
                sb.AppendLine($"    {primaryResult} = True");
            }
            else
            {
                sb.AppendLine($"    {primaryResult} = ser_{node.Id}.readline().decode().strip()");
            }
        }

        // =====================================================================
        // DATA PROCESSING
        // =====================================================================
        else if (MatchName(name, "String Parse", "Parse String", "String Split"))
        {
            var delimiter = GetPropStr(props, "Delimiter", "Separator");
            if (string.IsNullOrEmpty(delimiter)) delimiter = ",";
            sb.AppendLine($"    {primaryResult} = str({input1}).split({PyStr(delimiter)})");
        }
        else if (MatchName(name, "CSV Read", "Read CSV", "CSV Load"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"    with open({PyStr(path)}, 'r') as f_{node.Id}:");
            sb.AppendLine($"        reader_{node.Id} = csv.reader(f_{node.Id})");
            sb.AppendLine($"        {primaryResult} = list(reader_{node.Id})");
        }
        else if (MatchName(name, "CSV Write", "Write CSV", "CSV Save"))
        {
            var path = GetPropStr(props, "FilePath", "Path", "FileName");
            sb.AppendLine($"    with open({PyStr(path)}, 'w', newline='') as f_{node.Id}:");
            sb.AppendLine($"        writer_{node.Id} = csv.writer(f_{node.Id})");
            sb.AppendLine($"        writer_{node.Id}.writerows({input1})");
            sb.AppendLine($"    {primaryResult} = True");
        }
        else if (MatchName(name, "JSON Parse", "Parse JSON"))
        {
            sb.AppendLine($"    {primaryResult} = json.loads(str({input1}))");
        }
        else if (MatchName(name, "JSON Stringify", "To JSON"))
        {
            sb.AppendLine($"    {primaryResult} = json.dumps({input1})");
        }

        // =====================================================================
        // MEDIAPIPE
        // =====================================================================
        else if (MatchName(name, "MP Face Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            sb.AppendLine($"    mp_face_detection = mp.solutions.face_detection");
            sb.AppendLine($"    mp_drawing = mp.solutions.drawing_utils");
            sb.AppendLine($"    with mp_face_detection.FaceDetection(min_detection_confidence={confidence}) as face_det:");
            sb.AppendLine($"        rgb_{node.Id} = cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB)");
            sb.AppendLine($"        results_{node.Id} = face_det.process(rgb_{node.Id})");
            sb.AppendLine($"        {primaryResult} = {input1}.copy()");
            sb.AppendLine($"        h_, w_ = {input1}.shape[:2]");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]} = []  # face Rects");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]} = []  # scores");
            if (resultVars.Count > 3) sb.AppendLine($"        {resultVars[3]} = 0   # count");
            sb.AppendLine($"        if results_{node.Id}.detections:");
            if (resultVars.Count > 3) sb.AppendLine($"            {resultVars[3]} = len(results_{node.Id}.detections)");
            sb.AppendLine($"            for det in results_{node.Id}.detections:");
            sb.AppendLine($"                mp_drawing.draw_detection({primaryResult}, det)");
            if (resultVars.Count > 1) sb.AppendLine($"                bb = det.location_data.relative_bounding_box");
            if (resultVars.Count > 1) sb.AppendLine($"                {resultVars[1]}.append((int(bb.xmin*w_), int(bb.ymin*h_), int(bb.width*w_), int(bb.height*h_)))");
            if (resultVars.Count > 2) sb.AppendLine($"                {resultVars[2]}.append(det.score[0])");
        }
        else if (MatchName(name, "MP Face Mesh"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var drawContours = GetPropBool(props, "DrawContours", true);
            sb.AppendLine($"    mp_face_mesh = mp.solutions.face_mesh");
            sb.AppendLine($"    mp_drawing = mp.solutions.drawing_utils");
            sb.AppendLine($"    mp_drawing_styles = mp.solutions.drawing_styles");
            sb.AppendLine($"    with mp_face_mesh.FaceMesh(static_image_mode=True, max_num_faces=1, min_detection_confidence={confidence}) as face_mesh:");
            sb.AppendLine($"        rgb_{node.Id} = cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB)");
            sb.AppendLine($"        results_{node.Id} = face_mesh.process(rgb_{node.Id})");
            sb.AppendLine($"        {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]} = []  # landmarks");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]} = 0   # count");
            sb.AppendLine($"        if results_{node.Id}.multi_face_landmarks:");
            sb.AppendLine($"            for face_lm in results_{node.Id}.multi_face_landmarks:");
            if (drawContours) sb.AppendLine($"                mp_drawing.draw_landmarks({primaryResult}, face_lm, mp_face_mesh.FACEMESH_TESSELATION, mp_drawing_styles.get_default_face_mesh_tesselation_style())");
            sb.AppendLine($"                h_, w_ = {input1}.shape[:2]");
            if (resultVars.Count > 1) sb.AppendLine($"                {resultVars[1]} = [(int(lm.x*w_), int(lm.y*h_)) for lm in face_lm.landmark]");
            if (resultVars.Count > 2) sb.AppendLine($"                {resultVars[2]} = len(face_lm.landmark)");
        }
        else if (MatchName(name, "MP Hand Landmark"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var maxHands = GetPropNum(props, "MaxHands", 2);
            sb.AppendLine($"    mp_hands = mp.solutions.hands");
            sb.AppendLine($"    mp_drawing = mp.solutions.drawing_utils");
            sb.AppendLine($"    with mp_hands.Hands(static_image_mode=True, max_num_hands={maxHands}, min_detection_confidence={confidence}) as hands:");
            sb.AppendLine($"        rgb_{node.Id} = cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB)");
            sb.AppendLine($"        results_{node.Id} = hands.process(rgb_{node.Id})");
            sb.AppendLine($"        {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]} = []  # landmarks");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]} = 0   # count");
            sb.AppendLine($"        if results_{node.Id}.multi_hand_landmarks:");
            if (resultVars.Count > 2) sb.AppendLine($"            {resultVars[2]} = len(results_{node.Id}.multi_hand_landmarks)");
            sb.AppendLine($"            for hand_lm in results_{node.Id}.multi_hand_landmarks:");
            sb.AppendLine($"                mp_drawing.draw_landmarks({primaryResult}, hand_lm, mp_hands.HAND_CONNECTIONS)");
            sb.AppendLine($"                h_, w_ = {input1}.shape[:2]");
            if (resultVars.Count > 1) sb.AppendLine($"                {resultVars[1]} = [(int(lm.x*w_), int(lm.y*h_)) for lm in hand_lm.landmark]");
        }
        else if (MatchName(name, "MP Pose Landmark"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            sb.AppendLine($"    mp_pose = mp.solutions.pose");
            sb.AppendLine($"    mp_drawing = mp.solutions.drawing_utils");
            sb.AppendLine($"    mp_drawing_styles = mp.solutions.drawing_styles");
            sb.AppendLine($"    with mp_pose.Pose(static_image_mode=True, min_detection_confidence={confidence}) as pose:");
            sb.AppendLine($"        rgb_{node.Id} = cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB)");
            sb.AppendLine($"        results_{node.Id} = pose.process(rgb_{node.Id})");
            sb.AppendLine($"        {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]} = []  # landmarks");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]} = []  # visibility");
            if (resultVars.Count > 3) sb.AppendLine($"        {resultVars[3]} = 0   # count");
            sb.AppendLine($"        if results_{node.Id}.pose_landmarks:");
            sb.AppendLine($"            mp_drawing.draw_landmarks({primaryResult}, results_{node.Id}.pose_landmarks, mp_pose.POSE_CONNECTIONS, mp_drawing_styles.get_default_pose_landmarks_style())");
            sb.AppendLine($"            h_, w_ = {input1}.shape[:2]");
            if (resultVars.Count > 1) sb.AppendLine($"            {resultVars[1]} = [(int(lm.x*w_), int(lm.y*h_)) for lm in results_{node.Id}.pose_landmarks.landmark]");
            if (resultVars.Count > 2) sb.AppendLine($"            {resultVars[2]} = [lm.visibility for lm in results_{node.Id}.pose_landmarks.landmark]");
            if (resultVars.Count > 3) sb.AppendLine($"            {resultVars[3]} = len(results_{node.Id}.pose_landmarks.landmark)");
        }
        else if (MatchName(name, "MP Selfie Segmentation"))
        {
            var threshold = GetPropDouble(props, "Threshold", 0.5);
            var bgMode = GetPropStr(props, "BackgroundMode");
            var blurStrength = GetPropNum(props, "BlurStrength", 21);
            sb.AppendLine($"    mp_selfie = mp.solutions.selfie_segmentation");
            sb.AppendLine($"    with mp_selfie.SelfieSegmentation(model_selection=0) as seg:");
            sb.AppendLine($"        rgb_{node.Id} = cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB)");
            sb.AppendLine($"        results_{node.Id} = seg.process(rgb_{node.Id})");
            sb.AppendLine($"        mask_{node.Id} = (results_{node.Id}.segmentation_mask > {threshold}).astype(np.uint8)");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]} = (mask_{node.Id} * 255).astype(np.uint8)  # Mask output");
            if (string.IsNullOrEmpty(bgMode) || bgMode.Contains("Blur"))
            {
                sb.AppendLine($"        bg_{node.Id} = cv2.GaussianBlur({input1}, ({blurStrength}|1, {blurStrength}|1), 0)");
            }
            else if (bgMode.Contains("Green"))
            {
                sb.AppendLine($"        bg_{node.Id} = np.zeros_like({input1}); bg_{node.Id}[:] = (0, 255, 0)");
            }
            else
            {
                sb.AppendLine($"        bg_{node.Id} = np.zeros_like({input1})");
            }
            sb.AppendLine($"        mask3_{node.Id} = np.stack([mask_{node.Id}]*3, axis=-1)");
            sb.AppendLine($"        {primaryResult} = np.where(mask3_{node.Id}, {input1}, bg_{node.Id})");
        }
        else if (MatchName(name, "MP Object Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var maxDet = GetPropNum(props, "MaxDetections", 20);
            sb.AppendLine($"    mp_obj = mp.solutions.object_detection");
            sb.AppendLine($"    # NOTE: MediaPipe object detection requires mediapipe>=0.10");
            sb.AppendLine($"    # Alternative: use a simple ONNX model or Ultralytics YOLO");
            sb.AppendLine($"    base_options = mp.tasks.BaseOptions(model_asset_path='efficientdet_lite0.tflite')");
            sb.AppendLine($"    options = mp.tasks.vision.ObjectDetectorOptions(base_options=base_options, score_threshold={confidence}, max_results={maxDet})");
            sb.AppendLine($"    detector = mp.tasks.vision.ObjectDetector.create_from_options(options)");
            sb.AppendLine($"    mp_image_{node.Id} = mp.Image(image_format=mp.ImageFormat.SRGB, data=cv2.cvtColor({input1}, cv2.COLOR_BGR2RGB))");
            sb.AppendLine($"    det_result_{node.Id} = detector.detect(mp_image_{node.Id})");
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"    {resultVars[1]} = []  # bounding boxes");
            if (resultVars.Count > 2) sb.AppendLine($"    {resultVars[2]} = []  # labels");
            if (resultVars.Count > 3) sb.AppendLine($"    {resultVars[3]} = []  # scores");
            if (resultVars.Count > 4) sb.AppendLine($"    {resultVars[4]} = len(det_result_{node.Id}.detections)  # count");
            sb.AppendLine($"    for det in det_result_{node.Id}.detections:");
            sb.AppendLine($"        bb = det.bounding_box");
            sb.AppendLine($"        cv2.rectangle({primaryResult}, (bb.origin_x, bb.origin_y), (bb.origin_x+bb.width, bb.origin_y+bb.height), (0,255,0), 2)");
            sb.AppendLine($"        label = det.categories[0].category_name if det.categories else 'unknown'");
            sb.AppendLine($"        score = det.categories[0].score if det.categories else 0");
            sb.AppendLine($"        cv2.putText({primaryResult}, f'{{label}} {{score:.2f}}', (bb.origin_x, bb.origin_y-5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0,255,0), 1)");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]}.append((bb.origin_x, bb.origin_y, bb.width, bb.height))");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]}.append(label)");
            if (resultVars.Count > 3) sb.AppendLine($"        {resultVars[3]}.append(score)");
        }

        // =====================================================================
        // YOLO
        // =====================================================================
        else if (MatchName(name, "YOLOv8", "YOLO Detection", "YOLOv8 Detection"))
        {
            var confidence = GetPropDouble(props, "Confidence", 0.25);
            var iou = GetPropDouble(props, "IoUThreshold", "IoU", 0.45);
            var modelFile = GetPropStr(props, "ModelFile");
            if (string.IsNullOrEmpty(modelFile)) modelFile = "yolov8n.pt";
            sb.AppendLine($"    model_{node.Id} = YOLO({PyStr(modelFile)})");
            sb.AppendLine($"    results_{node.Id} = model_{node.Id}({input1}, conf={confidence}, iou={iou})[0]");
            sb.AppendLine($"    {primaryResult} = results_{node.Id}.plot()  # annotated image");
            if (resultVars.Count > 1) sb.AppendLine($"    {resultVars[1]} = []  # bounding boxes");
            if (resultVars.Count > 2) sb.AppendLine($"    {resultVars[2]} = []  # labels");
            if (resultVars.Count > 3) sb.AppendLine($"    {resultVars[3]} = []  # scores");
            if (resultVars.Count > 4) sb.AppendLine($"    {resultVars[4]} = len(results_{node.Id}.boxes)  # count");
            sb.AppendLine($"    for box in results_{node.Id}.boxes:");
            sb.AppendLine($"        x1, y1, x2, y2 = box.xyxy[0].cpu().numpy().astype(int)");
            if (resultVars.Count > 1) sb.AppendLine($"        {resultVars[1]}.append((x1, y1, x2-x1, y2-y1))");
            if (resultVars.Count > 2) sb.AppendLine($"        {resultVars[2]}.append(results_{node.Id}.names[int(box.cls[0])])");
            if (resultVars.Count > 3) sb.AppendLine($"        {resultVars[3]}.append(float(box.conf[0]))");
        }

        // =====================================================================
        // OCR
        // =====================================================================
        else if (MatchName(name, "PaddleOCR"))
        {
            var recThreshold = GetPropDouble(props, "RecThreshold", 0.5);
            sb.AppendLine($"    ocr_{node.Id} = PaddleOCR(use_angle_cls=True, lang='korean')  # change lang as needed");
            sb.AppendLine($"    ocr_result_{node.Id} = ocr_{node.Id}.ocr({input1}, cls=True)");
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"    {resultVars[1]} = []  # texts");
            if (resultVars.Count > 2) sb.AppendLine($"    {resultVars[2]} = []  # boxes");
            if (resultVars.Count > 3) sb.AppendLine($"    {resultVars[3]} = []  # scores");
            if (resultVars.Count > 4) sb.AppendLine($"    {resultVars[4]} = 0   # count");
            if (resultVars.Count > 5) sb.AppendLine($"    {resultVars[5]} = ''  # full text");
            sb.AppendLine($"    if ocr_result_{node.Id} and ocr_result_{node.Id}[0]:");
            if (resultVars.Count > 4) sb.AppendLine($"        {resultVars[4]} = len(ocr_result_{node.Id}[0])");
            sb.AppendLine($"        for line in ocr_result_{node.Id}[0]:");
            sb.AppendLine($"            box, (text, score) = line[0], line[1]");
            sb.AppendLine($"            if score >= {recThreshold}:");
            if (resultVars.Count > 1) sb.AppendLine($"                {resultVars[1]}.append(text)");
            if (resultVars.Count > 2) sb.AppendLine($"                pts = np.array(box, np.int32); {resultVars[2]}.append(tuple(cv2.boundingRect(pts)))");
            if (resultVars.Count > 3) sb.AppendLine($"                {resultVars[3]}.append(score)");
            sb.AppendLine($"                pts = np.array(box, np.int32)");
            sb.AppendLine($"                cv2.polylines({primaryResult}, [pts], True, (0,255,0), 2)");
            sb.AppendLine($"                cv2.putText({primaryResult}, text, (int(box[0][0]), int(box[0][1])-5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0,255,0), 1)");
            if (resultVars.Count > 5) sb.AppendLine($"        {resultVars[5]} = ' '.join({resultVars[1]})");
        }
        else if (MatchName(name, "Tesseract OCR", "Tesseract"))
        {
            var lang = GetPropStr(props, "Language");
            if (string.IsNullOrEmpty(lang)) lang = "eng+kor";
            var confidence = GetPropDouble(props, "Confidence", 0.5);
            var psm = GetPropNum(props, "PageSegMode", 3);
            sb.AppendLine($"    # Requires: pip install pytesseract && Tesseract-OCR installed on system");
            sb.AppendLine($"    # pytesseract.pytesseract.tesseract_cmd = r'C:\\Program Files\\Tesseract-OCR\\tesseract.exe'");
            sb.AppendLine($"    ocr_data_{node.Id} = pytesseract.image_to_data({input1}, lang={PyStr(lang)}, config='--psm {psm}', output_type=pytesseract.Output.DICT)");
            sb.AppendLine($"    {primaryResult} = {input1}.copy()");
            if (resultVars.Count > 1) sb.AppendLine($"    {resultVars[1]} = []  # texts");
            if (resultVars.Count > 2) sb.AppendLine($"    {resultVars[2]} = []  # boxes");
            if (resultVars.Count > 3) sb.AppendLine($"    {resultVars[3]} = []  # scores");
            if (resultVars.Count > 4) sb.AppendLine($"    {resultVars[4]} = 0   # count");
            if (resultVars.Count > 5) sb.AppendLine($"    {resultVars[5]} = ''  # full text");
            sb.AppendLine($"    for i_{node.Id} in range(len(ocr_data_{node.Id}['text'])):");
            sb.AppendLine($"        conf = float(ocr_data_{node.Id}['conf'][i_{node.Id}]) / 100.0");
            sb.AppendLine($"        txt = ocr_data_{node.Id}['text'][i_{node.Id}].strip()");
            sb.AppendLine($"        if conf >= {confidence} and txt:");
            if (resultVars.Count > 1) sb.AppendLine($"            {resultVars[1]}.append(txt)");
            sb.AppendLine($"            x, y, w, h = ocr_data_{node.Id}['left'][i_{node.Id}], ocr_data_{node.Id}['top'][i_{node.Id}], ocr_data_{node.Id}['width'][i_{node.Id}], ocr_data_{node.Id}['height'][i_{node.Id}]");
            if (resultVars.Count > 2) sb.AppendLine($"            {resultVars[2]}.append((x, y, w, h))");
            if (resultVars.Count > 3) sb.AppendLine($"            {resultVars[3]}.append(conf)");
            if (resultVars.Count > 4) sb.AppendLine($"            {resultVars[4]} += 1");
            sb.AppendLine($"            cv2.rectangle({primaryResult}, (x, y), (x+w, y+h), (0,255,0), 2)");
            sb.AppendLine($"            cv2.putText({primaryResult}, txt, (x, y-5), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0,255,0), 1)");
            if (resultVars.Count > 5) sb.AppendLine($"    {resultVars[5]} = ' '.join({resultVars[1]})");
        }

        // =====================================================================
        // AI VISION (LLM/VLM)
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
            sb.AppendLine($"    # OpenAI Vision API");
            sb.AppendLine($"    _, buf_{node.Id} = cv2.imencode('.png', {input1})");
            sb.AppendLine($"    img_b64_{node.Id} = base64.b64encode(buf_{node.Id}).decode('utf-8')");
            var promptVar = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : PyStr("Describe this image.");
            sb.AppendLine($"    prompt_{node.Id} = {promptVar}");
            sb.AppendLine($"    api_key_{node.Id} = {PyStr(apiKey)} or os.environ.get('OPENAI_API_KEY', '')");
            sb.AppendLine($"    resp_{node.Id} = requests.post('https://api.openai.com/v1/chat/completions',");
            sb.AppendLine($"        headers={{'Authorization': f'Bearer {{api_key_{node.Id}}}', 'Content-Type': 'application/json'}},");
            sb.AppendLine($"        json={{'model': {PyStr(model)}, 'max_tokens': {maxTokens}, 'temperature': {temperature},");
            sb.AppendLine($"              'messages': [{{'role': 'system', 'content': {PyStr(systemPrompt)}}},");
            sb.AppendLine($"                           {{'role': 'user', 'content': [");
            sb.AppendLine($"                               {{'type': 'text', 'text': prompt_{node.Id}}},");
            sb.AppendLine($"                               {{'type': 'image_url', 'image_url': {{'url': f'data:image/png;base64,{{img_b64_{node.Id}}}'}}}}]}}]}})");
            sb.AppendLine($"    {primaryResult} = resp_{node.Id}.json().get('choices', [{{}}])[0].get('message', {{}}).get('content', 'Error')");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"    {resultVars[1]} = {input1}.copy()");
                sb.AppendLine($"    cv2.putText({resultVars[1]}, str({primaryResult})[:80], (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0,255,0), 1)");
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
            sb.AppendLine($"    # Google Gemini Vision API");
            sb.AppendLine($"    _, buf_{node.Id} = cv2.imencode('.png', {input1})");
            sb.AppendLine($"    img_b64_{node.Id} = base64.b64encode(buf_{node.Id}).decode('utf-8')");
            var promptVar2 = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : PyStr("Describe this image.");
            sb.AppendLine($"    prompt_{node.Id} = {promptVar2}");
            sb.AppendLine($"    api_key_{node.Id} = {PyStr(apiKey)} or os.environ.get('GEMINI_API_KEY', '')");
            sb.AppendLine($"    resp_{node.Id} = requests.post(f'https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={{api_key_{node.Id}}}',");
            sb.AppendLine($"        json={{'system_instruction': {{'parts': [{{'text': {PyStr(systemPrompt)}}}]}},");
            sb.AppendLine($"              'generationConfig': {{'maxOutputTokens': {maxTokens}, 'temperature': {temperature}}},");
            sb.AppendLine($"              'contents': [{{'parts': [{{'text': prompt_{node.Id}}}, {{'inline_data': {{'mime_type': 'image/png', 'data': img_b64_{node.Id}}}}}]}}]}})");
            sb.AppendLine($"    {primaryResult} = resp_{node.Id}.json().get('candidates', [{{}}])[0].get('content', {{}}).get('parts', [{{}}])[0].get('text', 'Error')");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"    {resultVars[1]} = {input1}.copy()");
                sb.AppendLine($"    cv2.putText({resultVars[1]}, str({primaryResult})[:80], (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0,255,0), 1)");
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
            sb.AppendLine($"    # Anthropic Claude Vision API");
            sb.AppendLine($"    _, buf_{node.Id} = cv2.imencode('.png', {input1})");
            sb.AppendLine($"    img_b64_{node.Id} = base64.b64encode(buf_{node.Id}).decode('utf-8')");
            var promptVar3 = (node.Inputs.Count > 1 && inputVarMap.ContainsKey(node.Inputs[1].Name))
                ? inputVarMap[node.Inputs[1].Name]
                : PyStr("Describe this image.");
            sb.AppendLine($"    prompt_{node.Id} = {promptVar3}");
            sb.AppendLine($"    api_key_{node.Id} = {PyStr(apiKey)} or os.environ.get('ANTHROPIC_API_KEY', '')");
            sb.AppendLine($"    resp_{node.Id} = requests.post('https://api.anthropic.com/v1/messages',");
            sb.AppendLine($"        headers={{'x-api-key': api_key_{node.Id}, 'anthropic-version': '2023-06-01', 'Content-Type': 'application/json'}},");
            sb.AppendLine($"        json={{'model': {PyStr(model)}, 'max_tokens': {maxTokens}, 'temperature': {temperature},");
            sb.AppendLine($"              'system': {PyStr(systemPrompt)},");
            sb.AppendLine($"              'messages': [{{'role': 'user', 'content': [");
            sb.AppendLine($"                  {{'type': 'image', 'source': {{'type': 'base64', 'media_type': 'image/png', 'data': img_b64_{node.Id}}}}},");
            sb.AppendLine($"                  {{'type': 'text', 'text': prompt_{node.Id}}}]}}]}})");
            sb.AppendLine($"    {primaryResult} = resp_{node.Id}.json().get('content', [{{}}])[0].get('text', 'Error')");
            if (resultVars.Count > 1)
            {
                sb.AppendLine($"    {resultVars[1]} = {input1}.copy()");
                sb.AppendLine($"    cv2.putText({resultVars[1]}, str({primaryResult})[:80], (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0,255,0), 1)");
            }
        }

        // =====================================================================
        // UNKNOWN / FALLBACK
        // =====================================================================
        else
        {
            sb.AppendLine($"    # TODO: Unknown node type '{node.Name}' (category: {node.Category})");
            sb.AppendLine($"    # Inputs: {string.Join(", ", node.Inputs.Select(i => i.Name))}");
            sb.AppendLine($"    # Outputs: {string.Join(", ", node.Outputs.Select(o => o.Name))}");
            sb.AppendLine($"    # Properties: {string.Join(", ", node.Properties.Select(p => $"{p.Name}={p.Value}"))}");
            sb.AppendLine($"    {primaryResult} = None  # Placeholder");
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
        return $"None  # unconnected input {index}";
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

    private static string PyStr(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string PyBool(bool value) => value ? "True" : "False";
}
