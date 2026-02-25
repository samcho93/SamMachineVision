using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

[NodeInfo("ORB Features", NodeCategories.Feature, Description = "ORB (Oriented FAST and Rotated BRIEF) feature detection")]
public class ORBNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _descriptorsOutput = null!;
    private OutputPort<Mat> _keypointsImageOutput = null!;
    private NodeProperty _nFeatures = null!;
    private NodeProperty _scaleFactor = null!;
    private NodeProperty _nLevels = null!;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _descriptorsOutput = AddOutput<Mat>("Descriptors");
        _keypointsImageOutput = AddOutput<Mat>("Keypoints Image");
        _nFeatures = AddIntProperty("NFeatures", "Max Features", 500, 1, 10000, "Maximum number of features");
        _scaleFactor = AddFloatProperty("ScaleFactor", "Scale Factor", 1.2f, 1.01f, 2.0f, "Pyramid decimation ratio");
        _nLevels = AddIntProperty("NLevels", "Pyramid Levels", 8, 1, 20, "Number of pyramid levels");
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
            var scaleFactor = _scaleFactor.GetValue<float>();
            var nLevels = _nLevels.GetValue<int>();

            using var orb = ORB.Create(nFeatures, scaleFactor, nLevels);
            var descriptors = new Mat();
            orb.DetectAndCompute(image, null, out var keypoints, descriptors);

            var keypointsImage = new Mat();
            Cv2.DrawKeypoints(image, keypoints, keypointsImage, Scalar.All(-1), DrawMatchesFlags.DrawRichKeypoints);

            SetOutputValue(_descriptorsOutput, descriptors);
            SetOutputValue(_keypointsImageOutput, keypointsImage);
            SetPreview(keypointsImage);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"ORB error: {ex.Message}";
        }
    }
}
