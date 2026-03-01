using System.Collections.Concurrent;
using System.Text;
using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.OCR;

/// <summary>
/// Helper for PaddleOCR ONNX inference.
/// Handles detection (DB algorithm) and recognition (CTC decode) pipelines.
/// Models: PP-OCRv3/v4/v5 detection + recognition ONNX.
/// </summary>
public static class PaddleOCRHelper
{
    private static readonly ConcurrentDictionary<string, InferenceSession> _sessionCache = new();
    private static readonly ConcurrentDictionary<string, string[]> _dictCache = new();

    // ImageNet normalization for detection model
    private static readonly float[] DetMean = { 0.485f, 0.456f, 0.406f };
    private static readonly float[] DetStd = { 0.229f, 0.224f, 0.225f };

    #region Session Management

    public static InferenceSession GetSession(string modelFileName)
    {
        var modelPath = ResolveModelPath(modelFileName)
            ?? throw new FileNotFoundException(
                $"OCR model not found: {modelFileName}. " +
                $"Place model files in Models/OCR/ folder. " +
                $"Download from: huggingface.co/monkt/paddleocr-onnx");

        return _sessionCache.GetOrAdd(modelPath, path =>
        {
            var opts = new SessionOptions();
            opts.InterOpNumThreads = 1;
            opts.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            // Load from byte array to avoid P/Invoke marshaling issues
            // with non-ASCII metadata in PaddleOCR models
            var modelBytes = File.ReadAllBytes(path);
            return new InferenceSession(modelBytes, opts);
        });
    }

    /// <summary>
    /// Safely get input name from session, falling back to common PaddleOCR defaults.
    /// Some PaddleOCR models have non-ASCII tensor names that cause marshaling errors.
    /// </summary>
    public static string GetSafeInputName(InferenceSession session)
    {
        try
        {
            return session.InputNames[0];
        }
        catch
        {
            // PaddleOCR models commonly use "x" as input name
            return "x";
        }
    }

    /// <summary>
    /// Safely get input metadata dimensions, falling back to defaults.
    /// For dynamic dims (value &lt;= 0), uses the corresponding default value.
    /// </summary>
    public static int[] GetSafeInputDimensions(InferenceSession session, int[] defaults)
    {
        try
        {
            var inputName = GetSafeInputName(session);
            var meta = session.InputMetadata[inputName];
            var dims = meta.Dimensions;
            // Replace dynamic dims (-1 or 0) with defaults
            var result = new int[dims.Length];
            for (int i = 0; i < dims.Length; i++)
                result[i] = dims[i] > 0 ? dims[i] : (i < defaults.Length ? defaults[i] : 1);
            return result;
        }
        catch
        {
            return defaults;
        }
    }

    public static string[] LoadDictionary(string dictFile)
    {
        return _dictCache.GetOrAdd(dictFile, file =>
        {
            var dictPath = ResolveModelPath(file)
                ?? throw new FileNotFoundException(
                    $"Dictionary not found: {file}. Place in Models/OCR/ folder.");

            var lines = File.ReadAllLines(dictPath, System.Text.Encoding.UTF8)
                .Where(l => !string.IsNullOrEmpty(l))
                .ToArray();
            return lines;
        });
    }

    private static string? ResolveModelPath(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        var path = Path.Combine(baseDir, "Models", "OCR", fileName);
        if (File.Exists(path)) return path;

        path = Path.Combine(baseDir, fileName);
        if (File.Exists(path)) return path;

        path = Path.Combine("Models", "OCR", fileName);
        if (File.Exists(path)) return path;

        return null;
    }

    public static void DisposeAll()
    {
        foreach (var session in _sessionCache.Values)
            session.Dispose();
        _sessionCache.Clear();
        _dictCache.Clear();
    }

    #endregion

    #region Detection Preprocessing

    /// <summary>
    /// Preprocess image for PP-OCR detection model.
    /// Resizes so max side &lt;= maxSideLen, rounds to multiples of 32.
    /// Applies ImageNet normalization, returns NCHW RGB float array.
    /// </summary>
    public static (float[] Data, int ResizedH, int ResizedW) PreprocessForDetection(
        Mat image, int maxSideLen = 960)
    {
        int h = image.Height, w = image.Width;

        // Scale so max side <= maxSideLen
        float ratio = 1.0f;
        if (Math.Max(h, w) > maxSideLen)
            ratio = (float)maxSideLen / Math.Max(h, w);

        int newH = (int)(h * ratio);
        int newW = (int)(w * ratio);

        // Round to multiples of 32
        newH = Math.Max(32, ((newH + 31) / 32) * 32);
        newW = Math.Max(32, ((newW + 31) / 32) * 32);

        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);

        using var rgb = new Mat();
        if (resized.Channels() == 1)
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
        else
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        // NCHW with ImageNet normalization: (pixel/255 - mean) / std
        int hw = newH * newW;
        float[] data = new float[3 * hw];

        for (int y = 0; y < newH; y++)
        {
            for (int x = 0; x < newW; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                int offset = y * newW + x;
                data[offset] = (pixel.Item0 / 255f - DetMean[0]) / DetStd[0];          // R
                data[hw + offset] = (pixel.Item1 / 255f - DetMean[1]) / DetStd[1];     // G
                data[2 * hw + offset] = (pixel.Item2 / 255f - DetMean[2]) / DetStd[2]; // B
            }
        }

        return (data, newH, newW);
    }

    #endregion

    #region Detection Post-Processing (DB Algorithm)

    /// <summary>
    /// DB (Differentiable Binarization) post-processing.
    /// Extracts text region bounding boxes from probability map.
    /// </summary>
    public static List<(Rect Box, float Score)> DBPostProcess(
        float[] probMap, int mapH, int mapW, int origH, int origW,
        float binThresh = 0.3f, float boxThresh = 0.6f, float unclipRatio = 1.5f,
        int minSize = 3)
    {
        // Create probability Mat
        using var prob = new Mat(mapH, mapW, MatType.CV_32FC1);
        for (int y = 0; y < mapH; y++)
            for (int x = 0; x < mapW; x++)
            {
                int idx = y * mapW + x;
                prob.Set(y, x, idx < probMap.Length ? probMap[idx] : 0f);
            }

        // Binarize
        using var binary = new Mat();
        Cv2.Threshold(prob, binary, binThresh, 1.0, ThresholdTypes.Binary);

        using var binary8 = new Mat();
        binary.ConvertTo(binary8, MatType.CV_8UC1, 255);

        // Find contours
        Cv2.FindContours(binary8, out var contours, out _, RetrievalModes.List,
            ContourApproximationModes.ApproxSimple);

        float scaleX = (float)origW / mapW;
        float scaleY = (float)origH / mapH;

        var results = new List<(Rect Box, float Score)>();

        foreach (var contour in contours)
        {
            if (contour.Length < 4) continue;

            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < minSize || rect.Height < minSize) continue;

            // Calculate mean score in contour region
            float score = CalculateMeanScore(prob, contour, rect);
            if (score < boxThresh) continue;

            // Unclip: expand the bounding box
            float area = rect.Width * rect.Height;
            float perimeter = 2f * (rect.Width + rect.Height);
            float distance = area * unclipRatio / perimeter;

            int ex = (int)distance;
            int ey = (int)distance;

            int x1 = Math.Max(0, rect.X - ex);
            int y1 = Math.Max(0, rect.Y - ey);
            int x2 = Math.Min(mapW, rect.X + rect.Width + ex);
            int y2 = Math.Min(mapH, rect.Y + rect.Height + ey);

            // Map to original image coordinates
            var origRect = new Rect(
                (int)(x1 * scaleX), (int)(y1 * scaleY),
                (int)((x2 - x1) * scaleX), (int)((y2 - y1) * scaleY));

            // Clamp to image bounds
            origRect.X = Math.Max(0, origRect.X);
            origRect.Y = Math.Max(0, origRect.Y);
            origRect.Width = Math.Min(origRect.Width, origW - origRect.X);
            origRect.Height = Math.Min(origRect.Height, origH - origRect.Y);

            if (origRect.Width > 2 && origRect.Height > 2)
                results.Add((origRect, score));
        }

        return results;
    }

    private static float CalculateMeanScore(Mat prob, Point[] contour, Rect rect)
    {
        // Create mask for the contour area
        using var mask = new Mat(rect.Height, rect.Width, MatType.CV_8UC1, Scalar.Black);
        var shifted = contour.Select(p => new Point(p.X - rect.X, p.Y - rect.Y)).ToArray();
        Cv2.FillPoly(mask, new[] { shifted }, Scalar.White);

        // Extract ROI from probability map
        var roiRect = new Rect(
            Math.Max(0, rect.X),
            Math.Max(0, rect.Y),
            Math.Min(rect.Width, prob.Width - Math.Max(0, rect.X)),
            Math.Min(rect.Height, prob.Height - Math.Max(0, rect.Y)));

        if (roiRect.Width <= 0 || roiRect.Height <= 0) return 0;

        using var roi = new Mat(prob, roiRect);
        using var maskRoi = new Mat(mask, new Rect(0, 0, roiRect.Width, roiRect.Height));

        return (float)Cv2.Mean(roi, maskRoi).Val0;
    }

    #endregion

    #region Recognition Preprocessing

    /// <summary>
    /// Preprocess a cropped text region for PP-OCR recognition.
    /// Resizes to target height, keeps aspect ratio, pads width.
    /// Normalization: (pixel/255 - 0.5) / 0.5 = pixel/127.5 - 1 → range [-1, 1].
    /// Returns NCHW RGB float array.
    /// </summary>
    public static (float[] Data, int ActualW) PreprocessForRecognition(
        Mat textRegion, int targetH = 48, int maxW = 320)
    {
        int h = textRegion.Height, w = textRegion.Width;
        if (h <= 0 || w <= 0)
            return (new float[3 * targetH * maxW], maxW);

        // Resize to target height, keep aspect ratio
        float ratio = (float)targetH / h;
        int newW = Math.Max(1, Math.Min((int)(w * ratio), maxW));

        using var resized = new Mat();
        Cv2.Resize(textRegion, resized, new Size(newW, targetH), 0, 0, InterpolationFlags.Linear);

        // Pad to maxW with zeros (black)
        int padW = maxW;

        using var rgb = new Mat();
        if (resized.Channels() == 1)
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.GRAY2RGB);
        else
            Cv2.CvtColor(resized, rgb, ColorConversionCodes.BGR2RGB);

        int hw = targetH * padW;
        float[] data = new float[3 * hw];

        // Initialize to -1 (normalized zero padding)
        Array.Fill(data, -1f);

        for (int y = 0; y < targetH; y++)
        {
            for (int x = 0; x < newW; x++)
            {
                var pixel = rgb.At<Vec3b>(y, x);
                int offset = y * padW + x;
                data[offset] = pixel.Item0 / 127.5f - 1f;          // R
                data[hw + offset] = pixel.Item1 / 127.5f - 1f;     // G
                data[2 * hw + offset] = pixel.Item2 / 127.5f - 1f; // B
            }
        }

        return (data, padW);
    }

    #endregion

    #region CTC Decode

    /// <summary>
    /// CTC greedy decode: argmax per timestep, remove blanks and consecutive duplicates.
    /// PP-OCRv5: blank at index 0, dictionary chars at indices 1..N.
    /// PP-OCRv3: blank at last index (dict.Length), dictionary chars at indices 0..N-1.
    /// Auto-detects based on numChars vs dictionary.Length.
    /// </summary>
    public static (string Text, float Confidence) CTCDecode(
        float[] logits, int timesteps, int numChars, string[] dictionary)
    {
        var sb = new StringBuilder();
        int prevIdx = -1;
        float totalConf = 0;
        int charCount = 0;

        // PP-OCRv5: blank at index 0, chars start at index 1
        // PP-OCRv3: blank at last index, chars start at index 0
        // Detect: if numChars == dict.Length + 1, could be either.
        // Use blank-first (v5) as default since we use v5 models.
        bool blankFirst = true;
        int blankIdx = blankFirst ? 0 : dictionary.Length;

        for (int t = 0; t < timesteps; t++)
        {
            // Find argmax for this timestep
            int bestIdx = 0;
            float bestVal = logits[t * numChars];
            for (int c = 1; c < numChars; c++)
            {
                float val = logits[t * numChars + c];
                if (val > bestVal)
                {
                    bestVal = val;
                    bestIdx = c;
                }
            }

            // Skip blank and consecutive duplicates
            if (bestIdx != blankIdx && bestIdx != prevIdx)
            {
                // Map model index to dictionary index
                int charIdx = blankFirst ? bestIdx - 1 : bestIdx;
                if (charIdx >= 0 && charIdx < dictionary.Length)
                {
                    sb.Append(dictionary[charIdx]);
                    totalConf += bestVal;
                    charCount++;
                }
            }
            prevIdx = bestIdx;
        }

        float avgConf = charCount > 0 ? totalConf / charCount : 0;
        return (sb.ToString(), avgConf);
    }

    #endregion

    #region Text Region Sorting

    /// <summary>
    /// Sort text regions in reading order (top to bottom, left to right).
    /// Regions with significant vertical overlap are considered on the same line.
    /// </summary>
    public static void SortInReadingOrder(List<(Rect Box, float Score)> regions)
    {
        regions.Sort((a, b) =>
        {
            // Check if on the same line (vertical overlap > 50% of smaller height)
            int overlapH = Math.Min(a.Box.Y + a.Box.Height, b.Box.Y + b.Box.Height)
                         - Math.Max(a.Box.Y, b.Box.Y);
            int minH = Math.Min(a.Box.Height, b.Box.Height);

            if (overlapH > minH * 0.5)
                return a.Box.X.CompareTo(b.Box.X); // Same line: sort by X

            return a.Box.Y.CompareTo(b.Box.Y); // Different lines: sort by Y
        });
    }

    #endregion
}
