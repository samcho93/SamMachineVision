using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Segmentation;

[NodeInfo("Watershed", NodeCategories.Segmentation, Description = "Watershed segmentation")]
public class WatershedNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<Mat> _markersInput = null!;
    private OutputPort<Mat> _resultOutput = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _markersInput = AddInput<Mat>("Markers");
        _resultOutput = AddOutput<Mat>("Result");
    }

    public override void Process()
    {
        try
        {
            var image = GetInputValue(_imageInput);
            var markers = GetInputValue(_markersInput);

            if (image == null || image.Empty())
            {
                Error = "No input image";
                return;
            }

            if (image.Channels() != 3)
            {
                Error = "Watershed requires a 3-channel (BGR) image";
                return;
            }

            Mat markersMat;
            if (markers != null && !markers.Empty())
            {
                markersMat = markers.Clone();
                if (markersMat.Type() != MatType.CV_32SC1)
                    markersMat.ConvertTo(markersMat, MatType.CV_32SC1);
            }
            else
            {
                // Auto-generate markers from grayscale threshold + distance transform
                var gray = new Mat();
                Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);

                var thresh = new Mat();
                Cv2.Threshold(gray, thresh, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                gray.Dispose();

                var dist = new Mat();
                Cv2.DistanceTransform(thresh, dist, DistanceTypes.L2, DistanceTransformMasks.Mask5);
                thresh.Dispose();

                var distNorm = new Mat();
                Cv2.Normalize(dist, distNorm, 0, 255, NormTypes.MinMax);
                dist.Dispose();

                var distThresh = new Mat();
                distNorm.ConvertTo(distThresh, MatType.CV_8UC1);
                Cv2.Threshold(distThresh, distThresh, 128, 255, ThresholdTypes.Binary);
                distNorm.Dispose();

                markersMat = new Mat();
                Cv2.ConnectedComponents(distThresh, markersMat);
                distThresh.Dispose();
            }

            Cv2.Watershed(image, markersMat);

            // Convert markers to visualization
            var result = new Mat();
            markersMat.ConvertTo(result, MatType.CV_8UC1, 1, 0);
            Cv2.Normalize(result, result, 0, 255, NormTypes.MinMax);
            markersMat.Dispose();

            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Watershed error: {ex.Message}";
        }
    }
}
