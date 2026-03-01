using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MVXTester.Nodes.MediaPipe;

/// <summary>
/// Background mode for selfie segmentation result.
/// </summary>
public enum BackgroundMode
{
    Blur,
    Remove,
    Green
}

/// <summary>
/// Segments person from background using MediaPipe Selfie Segmentation model.
/// Supports blur, remove, or green screen background effects.
/// </summary>
[NodeInfo("MP Selfie Segmentation", NodeCategories.MediaPipe,
    Description = "Segment person from background using MediaPipe")]
public class MPSelfieSegmentationNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private OutputPort<Mat> _resultOutput = null!;
    private OutputPort<Mat> _maskOutput = null!;

    private NodeProperty _threshold = null!;
    private NodeProperty _bgMode = null!;
    private NodeProperty _blurStrength = null!;

    private const string ModelFile = "selfie_segmentation.onnx";
    private const int InputSize = 256;

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");

        _resultOutput = AddOutput<Mat>("Result");
        _maskOutput = AddOutput<Mat>("Mask");

        _threshold = AddDoubleProperty("Threshold", "Threshold", 0.5, 0.0, 1.0, "Segmentation threshold");
        _bgMode = AddEnumProperty("BackgroundMode", "Background Mode", BackgroundMode.Blur, "Background replacement effect");
        _blurStrength = AddIntProperty("BlurStrength", "Blur Strength", 21, 1, 99, "Gaussian blur kernel size (odd number)");
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

            var session = MediaPipeHelper.GetSession(ModelFile);
            var thresh = (float)_threshold.GetValue<double>();
            var bgMode = _bgMode.GetValue<BackgroundMode>();
            var blurSize = _blurStrength.GetValue<int>();
            if (blurSize % 2 == 0) blurSize++; // Must be odd

            // Detect input format from model metadata (NCHW vs NHWC)
            var inputName = session.InputNames[0];
            var inputShape = session.InputMetadata[inputName].Dimensions;
            bool isNCHW = inputShape.Length >= 4 && inputShape[1] == 3;

            float[] inputData;
            int[] tensorShape;
            if (isNCHW)
            {
                inputData = MediaPipeHelper.PreprocessImageNCHW(image, InputSize, InputSize);
                tensorShape = new[] { 1, 3, InputSize, InputSize };
            }
            else
            {
                inputData = MediaPipeHelper.PreprocessImageNHWC(image, InputSize, InputSize);
                tensorShape = new[] { 1, InputSize, InputSize, 3 };
            }

            var inputs = new List<NamedOnnxValue>
            {
                MediaPipeHelper.CreateTensor(inputName, inputData, tensorShape)
            };

            // Run inference
            using var results = session.Run(inputs);
            var outputs = results.ToList();

            // Parse output: segmentation mask - use flat array for safe access
            var maskFlat = MediaPipeHelper.GetFlatArray(outputs[0].AsTensor<float>());

            // Create mask Mat from flat array
            using var smallMask = new Mat(InputSize, InputSize, MatType.CV_32FC1);
            for (int y = 0; y < InputSize; y++)
            {
                for (int x = 0; x < InputSize; x++)
                {
                    int idx = y * InputSize + x;
                    float val = idx < maskFlat.Length ? maskFlat[idx] : 0;
                    smallMask.Set(y, x, val);
                }
            }

            // Resize mask to original image size
            using var fullMask = new Mat();
            Cv2.Resize(smallMask, fullMask, new Size(image.Width, image.Height), 0, 0, InterpolationFlags.Linear);

            // Apply threshold
            using var binaryMask = new Mat();
            Cv2.Threshold(fullMask, binaryMask, thresh, 1.0, ThresholdTypes.Binary);

            // Convert to 8-bit for blending
            using var mask8 = new Mat();
            binaryMask.ConvertTo(mask8, MatType.CV_8UC1, 255);

            // Output mask
            var outputMask = mask8.Clone();

            // Apply background effect
            var bgImage = image.Clone();
            if (bgImage.Channels() == 1)
                Cv2.CvtColor(bgImage, bgImage, ColorConversionCodes.GRAY2BGR);

            Mat result;
            switch (bgMode)
            {
                case BackgroundMode.Blur:
                    using (var blurred = new Mat())
                    {
                        Cv2.GaussianBlur(bgImage, blurred, new Size(blurSize, blurSize), 0);
                        result = BlendWithMask(bgImage, blurred, mask8);
                    }
                    break;

                case BackgroundMode.Remove:
                    result = new Mat(bgImage.Size(), bgImage.Type(), new Scalar(0, 0, 0));
                    bgImage.CopyTo(result, mask8);
                    break;

                case BackgroundMode.Green:
                    using (var green = new Mat(bgImage.Size(), bgImage.Type(), new Scalar(0, 255, 0)))
                    {
                        result = BlendWithMask(bgImage, green, mask8);
                    }
                    break;

                default:
                    result = bgImage;
                    break;
            }

            bgImage.Dispose();

            SetOutputValue(_resultOutput, result);
            SetOutputValue(_maskOutput, outputMask);
            SetPreview(result);
            Error = null;
        }
        catch (FileNotFoundException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            Error = $"MP Selfie Segmentation error: {ex.Message}";
        }
    }

    /// <summary>
    /// Blend foreground and background using a mask.
    /// Where mask=255 → foreground, mask=0 → background.
    /// </summary>
    private static Mat BlendWithMask(Mat foreground, Mat background, Mat mask)
    {
        var result = background.Clone();
        foreground.CopyTo(result, mask);
        return result;
    }
}
