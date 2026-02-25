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
        if (mat == null || mat.IsDisposed || mat.Empty())
        {
            OutputImage = null;
            return;
        }

        try
        {
            // mat is expected to be a safe snapshot (already cloned by caller)
            Mat display;
            if (mat.Channels() == 1)
            {
                display = new Mat();
                Cv2.CvtColor(mat, display, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                display = mat;
            }
            OutputImage = display.ToWriteableBitmap();
            if (display != mat) display.Dispose();
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
