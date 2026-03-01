using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.AI;

/// <summary>
/// OpenAI Vision (GPT-4o) node for image analysis using the Chat Completions API.
/// Sends image + prompt to OpenAI and returns the text response.
/// Requires an API key from platform.openai.com
/// </summary>
[NodeInfo("OpenAI Vision", NodeCategories.LLMVLM,
    Description = "Analyze images using OpenAI GPT-4o Vision API")]
public class OpenAIVisionNode : BaseNode
{
    private InputPort<Mat> _imageInput = null!;
    private InputPort<string> _promptInput = null!;
    private OutputPort<string> _responseOutput = null!;
    private OutputPort<Mat> _resultOutput = null!;

    private NodeProperty _apiKey = null!;
    private NodeProperty _model = null!;
    private NodeProperty _maxTokens = null!;
    private NodeProperty _temperature = null!;
    private NodeProperty _systemPrompt = null!;

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    protected override void Setup()
    {
        _imageInput = AddInput<Mat>("Image");
        _promptInput = AddInput<string>("Prompt");

        _responseOutput = AddOutput<string>("Response");
        _resultOutput = AddOutput<Mat>("Result");

        _apiKey = AddStringProperty("ApiKey", "API Key", "",
            "OpenAI API key (sk-...)");
        _model = AddStringProperty("Model", "Model", "gpt-4o-mini",
            "gpt-4o, gpt-4o-mini, gpt-4.1-nano, gpt-4.1-mini");
        _maxTokens = AddIntProperty("MaxTokens", "Max Tokens", 1024, 1, 4096,
            "Maximum response tokens");
        _temperature = AddDoubleProperty("Temperature", "Temperature", 0.7, 0.0, 2.0,
            "Sampling temperature");
        _systemPrompt = AddStringProperty("SystemPrompt", "System Prompt", "You are a helpful vision assistant. Describe what you see in the image.",
            "System prompt for the model");

        // Auto-load from api_config.json
        LoadApiConfig();
    }

    private void LoadApiConfig()
    {
        var config = ApiConfigHelper.GetConfig("openai");
        if (config == null) return;

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _apiKey.Value = config.ApiKey;
        if (!string.IsNullOrWhiteSpace(config.Model))
            _model.Value = config.Model;
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

            var key = _apiKey.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                Error = "API Key is required. Get one from platform.openai.com";
                return;
            }

            var prompt = _promptInput.IsConnected
                ? GetInputValue(_promptInput) ?? "Describe this image."
                : "Describe this image.";

            var model = _model.GetValue<string>();
            var maxTokens = _maxTokens.GetValue<int>();
            var temperature = _temperature.GetValue<double>();
            var systemPrompt = _systemPrompt.GetValue<string>();

            // Encode image to base64
            Cv2.ImEncode(".png", image, out byte[] imageBytes);
            var base64Image = Convert.ToBase64String(imageBytes);

            // Build request
            var requestBody = new
            {
                model,
                max_tokens = maxTokens,
                temperature,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new
                            {
                                type = "image_url",
                                image_url = new { url = $"data:image/png;base64,{base64Image}" }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            var response = _httpClient.Send(request);
            var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var errorDoc = JsonDocument.Parse(responseJson);
                var errorMsg = errorDoc.RootElement
                    .GetProperty("error")
                    .GetProperty("message")
                    .GetString();
                Error = $"OpenAI API error: {errorMsg}";
                return;
            }

            var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            // Draw response on image
            var result = image.Clone();
            if (result.Channels() == 1)
                Cv2.CvtColor(result, result, ColorConversionCodes.GRAY2BGR);

            DrawResponseOverlay(result, content, model);

            SetOutputValue(_responseOutput, content);
            SetOutputValue(_resultOutput, result);
            SetPreview(result);
            SetTextPreview($"[{model}]\n{content}");
            Error = null;
        }
        catch (TaskCanceledException)
        {
            Error = "OpenAI API request timed out";
        }
        catch (Exception ex)
        {
            Error = $"OpenAI Vision: {ex.Message}";
        }
    }

    private static void DrawResponseOverlay(Mat image, string response, string model)
    {
        // Semi-transparent background at bottom
        int boxH = Math.Min(80, image.Height / 3);
        var roi = new Rect(0, image.Height - boxH, image.Width, boxH);
        using var overlay = new Mat(image, roi);
        overlay.SetTo(new Scalar(40, 40, 40));
        Cv2.AddWeighted(overlay, 0.7, new Mat(image, roi), 0.3, 0, overlay);

        // Truncate response for display
        var displayText = response.Length > 100
            ? response[..100].Replace('\n', ' ') + "..."
            : response.Replace('\n', ' ');

        // ASCII-safe label
        var safeText = ToAscii(displayText, 80);

        Cv2.PutText(image, $"[{model}]", new Point(10, image.Height - boxH + 20),
            HersheyFonts.HersheySimplex, 0.5, new Scalar(0, 200, 255), 1, LineTypes.AntiAlias);
        Cv2.PutText(image, safeText, new Point(10, image.Height - boxH + 50),
            HersheyFonts.HersheySimplex, 0.4, new Scalar(255, 255, 255), 1, LineTypes.AntiAlias);
    }

    private static string ToAscii(string text, int maxLen = 80)
    {
        var sb = new StringBuilder(Math.Min(text.Length, maxLen));
        int count = 0;
        foreach (char c in text)
        {
            if (count >= maxLen) { sb.Append(".."); break; }
            sb.Append(c >= 32 && c <= 126 ? c : '?');
            count++;
        }
        return sb.ToString();
    }
}
