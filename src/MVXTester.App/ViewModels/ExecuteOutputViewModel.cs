using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using MVXTester.Core.Models;

namespace MVXTester.App.ViewModels;

public partial class ExecuteOutputViewModel : ObservableObject
{
    [ObservableProperty] private WriteableBitmap? _outputImage;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _fpsText = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private ObservableCollection<string> _logMessages = new();

    public event Action<MouseEventData>? MouseEventOccurred;
    public event Action<KeyboardEventData>? KeyboardEventOccurred;

    public void UpdateImage(Mat? mat)
    {
        if (mat == null || mat.Empty())
        {
            OutputImage = null;
            return;
        }

        try
        {
            var display = mat.Clone();
            if (display.Channels() == 1)
            {
                var bgr = new Mat();
                Cv2.CvtColor(display, bgr, ColorConversionCodes.GRAY2BGR);
                display.Dispose();
                display = bgr;
            }
            OutputImage = display.ToWriteableBitmap();
            display.Dispose();
        }
        catch
        {
            OutputImage = null;
        }
    }

    public void AddLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogMessages.Add(timestamped);
        if (LogMessages.Count > 1000)
            LogMessages.RemoveAt(0);
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }

    public void RaiseMouseEvent(MouseEventData data)
    {
        MouseEventOccurred?.Invoke(data);
    }

    public void RaiseKeyboardEvent(KeyboardEventData data)
    {
        KeyboardEventOccurred?.Invoke(data);
    }
}
