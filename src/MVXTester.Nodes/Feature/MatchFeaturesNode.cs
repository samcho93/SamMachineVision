using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Feature;

public enum MatcherType
{
    BruteForce,
    BruteForceHamming,
    FlannBased
}

[NodeInfo("Match Features", NodeCategories.Feature, Description = "Match feature descriptors between two images")]
public class MatchFeaturesNode : BaseNode
{
    private InputPort<Mat> _desc1Input = null!;
    private InputPort<Mat> _desc2Input = null!;
    private InputPort<Mat> _image1Input = null!;
    private InputPort<Mat> _image2Input = null!;
    private OutputPort<Mat> _matchesImageOutput = null!;
    private NodeProperty _matcherType = null!;
    private NodeProperty _maxMatches = null!;

    protected override void Setup()
    {
        _desc1Input = AddInput<Mat>("Desc1");
        _desc2Input = AddInput<Mat>("Desc2");
        _image1Input = AddInput<Mat>("Image1");
        _image2Input = AddInput<Mat>("Image2");
        _matchesImageOutput = AddOutput<Mat>("Matches Image");
        _matcherType = AddEnumProperty("MatcherType", "Matcher", MatcherType.BruteForce, "Feature matching algorithm");
        _maxMatches = AddIntProperty("MaxMatches", "Max Matches", 50, 1, 1000, "Maximum number of matches to display");
    }

    public override void Process()
    {
        try
        {
            var desc1 = GetInputValue(_desc1Input);
            var desc2 = GetInputValue(_desc2Input);
            var image1 = GetInputValue(_image1Input);
            var image2 = GetInputValue(_image2Input);

            if (desc1 == null || desc1.Empty() || desc2 == null || desc2.Empty())
            {
                Error = "Descriptors required";
                return;
            }

            var matcherType = _matcherType.GetValue<MatcherType>();
            var maxMatches = _maxMatches.GetValue<int>();

            DescriptorMatcher matcher = matcherType switch
            {
                MatcherType.BruteForceHamming => DescriptorMatcher.Create("BruteForce-Hamming"),
                MatcherType.FlannBased => DescriptorMatcher.Create("FlannBased"),
                _ => DescriptorMatcher.Create("BruteForce")
            };

            var matches = matcher.Match(desc1, desc2);
            matcher.Dispose();

            // Sort by distance and take top N
            var sortedMatches = matches.OrderBy(m => m.Distance).Take(maxMatches).ToArray();

            var matchesImage = new Mat();
            Cv2.DrawMatches(
                image1 ?? new Mat(),
                Array.Empty<KeyPoint>(),
                image2 ?? new Mat(),
                Array.Empty<KeyPoint>(),
                sortedMatches,
                matchesImage);

            SetOutputValue(_matchesImageOutput, matchesImage);
            SetPreview(matchesImage);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Match Features error: {ex.Message}";
        }
    }
}
