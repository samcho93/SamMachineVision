using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.App.ViewModels;

public partial class NodeViewModel : ObservableObject
{
    [ObservableProperty] private System.Windows.Point _location;
    [ObservableProperty] private WriteableBitmap? _previewImage;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private System.Windows.Size _desiredSize;

    public INode Model { get; }
    public string Title => Model.Name;
    public string Category => Model.Category;
    public ObservableCollection<ConnectorViewModel> InputConnectors { get; } = new();
    public ObservableCollection<ConnectorViewModel> OutputConnectors { get; } = new();

    public SolidColorBrush CategoryColor => CategoryColorHelper.GetHeaderBrush(Category);
    public SolidColorBrush CategoryBorderColor => CategoryColorHelper.GetBorderBrush(Category);

    public NodeViewModel(INode model)
    {
        Model = model;

        foreach (var input in model.Inputs)
        {
            InputConnectors.Add(new ConnectorViewModel(
                input.Name, input.DataType, true, this, inputPort: input));
        }

        foreach (var output in model.Outputs)
        {
            OutputConnectors.Add(new ConnectorViewModel(
                output.Name, output.DataType, false, this, outputPort: output));
        }
    }

    public void UpdatePreview()
    {
        ErrorMessage = Model.Error;

        try
        {
            using var snapshot = Model.ClonePreview();
            if (snapshot != null)
            {
                Mat preview;
                var scale = Math.Min(160.0 / snapshot.Width, 120.0 / snapshot.Height);
                if (scale < 1.0)
                {
                    preview = new Mat();
                    Cv2.Resize(snapshot, preview, new OpenCvSharp.Size(0, 0), scale, scale);
                }
                else
                {
                    preview = snapshot.Clone();
                }

                if (preview.Channels() == 1)
                {
                    var bgr = new Mat();
                    Cv2.CvtColor(preview, bgr, ColorConversionCodes.GRAY2BGR);
                    preview.Dispose();
                    preview = bgr;
                }

                PreviewImage = preview.ToWriteableBitmap();
                preview.Dispose();
            }
            else
            {
                PreviewImage = null;
            }
        }
        catch
        {
            PreviewImage = null;
        }

        foreach (var c in InputConnectors)
            c.IsConnected = Model.Inputs.First(i => i.Name == c.Name).IsConnected;
        foreach (var c in OutputConnectors)
            c.IsConnected = Model.Outputs.First(o => o.Name == c.Name).Connections.Count > 0;
    }
}

public static class CategoryColorHelper
{
    private static readonly Dictionary<string, SolidColorBrush> _brushes = new()
    {
        [NodeCategories.Input]        = Frozen(0x74, 0xC7, 0xEC),
        [NodeCategories.Color]        = Frozen(0xCB, 0xA6, 0xF7),
        [NodeCategories.Filter]       = Frozen(0x89, 0xB4, 0xFA),
        [NodeCategories.Edge]         = Frozen(0xF3, 0x8B, 0xA8),
        [NodeCategories.Morphology]   = Frozen(0xFA, 0xB3, 0x87),
        [NodeCategories.Threshold]    = Frozen(0xF9, 0xE2, 0xAF),
        [NodeCategories.Contour]      = Frozen(0xA6, 0xE3, 0xA1),
        [NodeCategories.Feature]      = Frozen(0x94, 0xE2, 0xD5),
        [NodeCategories.Drawing]      = Frozen(0xF5, 0xC2, 0xE7),
        [NodeCategories.Transform]    = Frozen(0x89, 0xDC, 0xEB),
        [NodeCategories.Histogram]    = Frozen(0xB4, 0xBE, 0xFE),
        [NodeCategories.Arithmetic]   = Frozen(0xEB, 0xA0, 0xAC),
        [NodeCategories.Detection]    = Frozen(0xF2, 0xCD, 0xCD),
        [NodeCategories.Segmentation] = Frozen(0xA6, 0xE3, 0xA1),
        [NodeCategories.Value]        = Frozen(0x93, 0x99, 0xB2),
        [NodeCategories.Control]      = Frozen(0x74, 0xC7, 0xEC),
        [NodeCategories.Communication]= Frozen(0xF9, 0xE2, 0xAF),
        [NodeCategories.Data]         = Frozen(0x94, 0xE2, 0xD5),
        [NodeCategories.Event]        = Frozen(0xF5, 0xC2, 0xE7),
        [NodeCategories.Script]       = Frozen(0xF2, 0xCD, 0xCD),
    };

    private static readonly SolidColorBrush _default = Frozen(0x45, 0x45, 0x5A);

    public static SolidColorBrush GetBrush(string category)
        => _brushes.TryGetValue(category, out var brush) ? brush : _default;

    public static SolidColorBrush GetHeaderBrush(string category)
    {
        var src = GetBrush(category);
        var c = src.Color;
        var r = (byte)(c.R * 0.4 + 0x2B * 0.6);
        var g = (byte)(c.G * 0.4 + 0x2B * 0.6);
        var b = (byte)(c.B * 0.4 + 0x3D * 0.6);
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static SolidColorBrush GetBorderBrush(string category)
    {
        var src = GetBrush(category);
        var c = src.Color;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x60, c.R, c.G, c.B));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
