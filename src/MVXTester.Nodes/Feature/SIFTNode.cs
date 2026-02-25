using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

[NodeInfo("SIFT Features", NodeCategories.Feature, Description = "SIFT (Scale-Invariant Feature Transform) feature detection")]
public class SIFTNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _descriptorsOutput = null!;
    private OutputPort<Mat> _keypointsImageOutput = null!;
    private NodeProperty _nFeatures = null!;
    private NodeProperty _nOctaveLayers = null!;
    private NodeProperty _contrastThreshold = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _descriptorsOutput = AddOutput<Mat>("Descriptors");
        _keypointsImageOutput = AddOutput<Mat>("Keypoints Image");
        _nFeatures = AddIntProperty("NFeatures", "Max Features", 0, 0, 10000, "Maximum features (0 = unlimited)");
        _nOctaveLayers = AddIntProperty("NOctaveLayers", "Octave Layers", 3, 1, 10, "Number of layers per octave");
        _contrastThreshold = AddDoubleProperty("ContrastThreshold", "Contrast Threshold", 0.04, 0.0, 1.0, "Contrast threshold");
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

            var nFeatures = _nFeatures.GetValue<int>();
            var nOctaveLayers = _nOctaveLayers.GetValue<int>();
            var contrastThreshold = _contrastThreshold.GetValue<double>();

            using var sift = OpenCvSharp.Features2D.SIFT.Create(nFeatures, nOctaveLayers, contrastThreshold);
            var descriptors = new Mat();
            sift.DetectAndCompute(image, null, out var keypoints, descriptors);

            var keypointsImage = new Mat();
            Cv2.DrawKeypoints(image, keypoints, keypointsImage, Scalar.All(-1), DrawMatchesFlags.DrawRichKeypoints);

            SetOutputValue(_descriptorsOutput, descriptors);
            SetOutputValue(_keypointsImageOutput, keypointsImage);
            SetPreview(keypointsImage);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"SIFT error: {ex.Message}";
        }
    }
}
