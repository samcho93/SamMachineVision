using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Inspection;

[NodeInfo("Face Detector", NodeCategories.Inspection,
    Description = "Detects human faces using Haar cascade classifier")]
public class FaceDetectorNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Rect[]> _facesOutput = null!;
    private OutputPort<Point[]> _centersOutput = null!;
    private OutputPort<int> _countOutput = null!;

    private NodeProperty _scaleFactor = null!;
    private NodeProperty _minNeighbors = null!;
    private NodeProperty _minFaceSize = null!;
    private NodeProperty _maxFaceSize = null!;
    private NodeProperty _cascadeFile = null!;

    private CascadeClassifier? _classifier;
    private string _lastCascadePath = "";

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _facesOutput = AddOutput<Rect[]>("Faces");
        _centersOutput = AddOutput<Point[]>("Centers");
        _countOutput = AddOutput<int>("Count");

        _scaleFactor = AddDoubleProperty("ScaleFactor", "Scale Factor", 1.1, 1.01, 2.0, "Scale factor for multi-scale detection");
        _minNeighbors = AddIntProperty("MinNeighbors", "Min Neighbors", 5, 1, 20, "Min neighbors for reliable detection");
        _minFaceSize = AddIntProperty("MinFaceSize", "Min Face Size", 30, 10, 1000, "Minimum face size in pixels");
        _maxFaceSize = AddIntProperty("MaxFaceSize", "Max Face Size", 0, 0, 5000, "Maximum face size (0=unlimited)");
        _cascadeFile = AddStringProperty("CascadeFile", "Cascade File", "haarcascade_frontalface_default.xml", "Cascade classifier file name");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            var scaleFactor = _scaleFactor.GetValue<double>();
            var minNeighbors = _minNeighbors.GetValue<int>();
            var minFaceSize = _minFaceSize.GetValue<int>();
            var maxFaceSize = _maxFaceSize.GetValue<int>();
            var cascadeFileName = _cascadeFile.GetValue<string>();

            if (string.IsNullOrWhiteSpace(cascadeFileName))
            {
                Error = "Cascade file name is empty";
                return;
            }

            // Resolve cascade file path
            var cascadePath = ResolveCascadePath(cascadeFileName);
            if (cascadePath == null)
            {
                Error = $"Cascade file not found: {cascadeFileName}. Place the file in the application directory or data subfolder.";
                return;
            }

            // Load classifier if needed
            if (_classifier == null || cascadePath != _lastCascadePath)
            {
                _classifier?.Dispose();
                _classifier = new CascadeClassifier(cascadePath);
                if (_classifier.Empty())
                {
                    Error = $"Failed to load cascade file: {cascadePath}";
                    _classifier.Dispose();
                    _classifier = null;
                    return;
                }
                _lastCascadePath = cascadePath;
            }

            // Convert to grayscale
            using var gray = new Mat();
            if (image.Channels() > 1)
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            else
                image.CopyTo(gray);

            // Resize for speed (process at reduced resolution if image is large)
            double scale = 1.0;
            using var processGray = new Mat();
            if (gray.Cols > 800)
            {
                scale = 800.0 / gray.Cols;
                Cv2.Resize(gray, processGray, new Size(), scale, scale, InterpolationFlags.Linear);
            }
            else
            {
                gray.CopyTo(processGray);
            }

            // Equalize histogram for better detection
            Cv2.EqualizeHist(processGray, processGray);

            // Detect faces
            var minSize = new Size(
                (int)(minFaceSize * scale),
                (int)(minFaceSize * scale));
            var maxSize = maxFaceSize > 0
                ? new Size((int)(maxFaceSize * scale), (int)(maxFaceSize * scale))
                : new Size(0, 0);

            var detections = _classifier.DetectMultiScale(
                processGray, scaleFactor, minNeighbors,
                HaarDetectionTypes.ScaleImage, minSize, maxSize);

            // Scale coordinates back to original resolution
            var faceList = new List<Rect>();
            var centerList = new List<Point>();

            foreach (var det in detections)
            {
                var face = new Rect(
                    (int)(det.X / scale),
                    (int)(det.Y / scale),
                    (int)(det.Width / scale),
                    (int)(det.Height / scale));
                faceList.Add(face);
                centerList.Add(new Point(face.X + face.Width / 2, face.Y + face.Height / 2));
            }

            var faces = faceList.ToArray();
            var centers = centerList.ToArray();
            var count = faces.Length;

            // Build result image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            for (int i = 0; i < count; i++)
            {
                // Green rectangle around face
                Cv2.Rectangle(result, faces[i], new Scalar(0, 255, 0), 2);

                // Label with face number
                var label = $"Face #{i + 1}";
                Cv2.PutText(result, label, new Point(faces[i].X, faces[i].Y - 5),
                    HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 255, 0), 2);
            }

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_facesOutput, faces);
            SetOutputValue(_centersOutput, centers);
            SetOutputValue(_countOutput, count);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Face Detector error: {ex.Message}";
        }
    }

    private static string? ResolveCascadePath(string fileName)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Try direct path (in case user provided full path)
        if (Path.IsPathRooted(fileName) && File.Exists(fileName))
            return fileName;

        // Try application base directory
        var path = Path.Combine(baseDir, fileName);
        if (File.Exists(path)) return path;

        // Try data subfolder
        path = Path.Combine(baseDir, "data", fileName);
        if (File.Exists(path)) return path;

        // Try OpenCvSharp data folder
        path = Path.Combine(baseDir, "runtimes", "win-x64", "native", fileName);
        if (File.Exists(path)) return path;

        return null;
    }

    public override void Cleanup()
    {
        _classifier?.Dispose();
        _classifier = null;
        _lastCascadePath = "";
        base.Cleanup();
    }
}
