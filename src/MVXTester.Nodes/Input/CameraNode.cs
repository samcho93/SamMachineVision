using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Input;

/// <summary>
/// Unified trigger mode for all camera backends.
/// </summary>
public enum CameraTriggerMode
{
    Continuous,
    Software,
    Hardware
}

/// <summary>
/// Unified pixel format for all camera backends.
/// </summary>
public enum CameraPixelFormat
{
    Auto,
    Mono8,
    BayerRG8,
    BayerGR8,
    BayerBG8,
    BayerGB8,
    RGB8,
    BGR8,
    Color24,
    YUV422_8
}

/// <summary>
/// Unified camera node that integrates USB, HIK, and Cognex camera backends.
/// Shows all connected cameras from all available backends in a single device list.
/// Internally delegates to the appropriate backend node based on user selection.
/// </summary>
[NodeInfo("Camera", NodeCategories.Input, Description = "Unified camera capture (USB, HIK, Cognex)")]
public class CameraNode : BaseNode, IStreamingSource, IDeviceEnumerable
{
    // Ports
    private InputPort<int> _triggerInput = null!;
    private OutputPort<Mat> _frameOutput = null!;

    // Properties - Device selection
    private NodeProperty _deviceList = null!;

    // Properties - Common
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;

    // Properties - USB specific
    private NodeProperty _fps = null!;
    private NodeProperty _usbBackend = null!;

    // Properties - Industrial (HIK/Cognex)
    private NodeProperty _triggerMode = null!;
    private NodeProperty _exposureTime = null!;
    private NodeProperty _pixelFormat = null!;

    // Properties - HIK specific
    private NodeProperty _gain = null!;
    private NodeProperty _autoExposure = null!;
    private NodeProperty _autoGain = null!;
    private NodeProperty _gammaEnable = null!;
    private NodeProperty _gamma = null!;
    private NodeProperty _reverseX = null!;
    private NodeProperty _reverseY = null!;

    // Properties - Cognex specific
    private NodeProperty _brightness = null!;

    // Backend node instances (created lazily during enumeration)
    private BaseNode? _usbNode;
    private BaseNode? _hikNode;
    private BaseNode? _cognexNode;

    // Device tracking
    private record DeviceEntry(string DisplayName, int GlobalIndex, int BackendIndex, string BackendType);
    private List<DeviceEntry> _allDevices = new();
    private BaseNode? _activeBackend;
    private string _activeBackendType = "";
    private int _activeBackendIndex = -1;

    protected override void Setup()
    {
        // Ports
        _triggerInput = AddInput<int>("Trigger");
        _frameOutput = AddOutput<Mat>("Frame");

        // Device selection
        _deviceList = AddDeviceListProperty("DeviceList", "Camera", -1, "Select camera device");

        // Common
        _width = AddIntProperty("Width", "Width", 640, 0, 10000, "Capture width (0=default/max)");
        _height = AddIntProperty("Height", "Height", 480, 0, 10000, "Capture height (0=default/max)");

        // USB
        _fps = AddIntProperty("FPS", "FPS", 30, 1, 120, "Target FPS (USB only)");
        _usbBackend = AddEnumProperty("Backend", "USB Backend", UsbCameraBackend.DirectShow, "Capture backend API (USB only)");

        // Industrial
        _triggerMode = AddEnumProperty("TriggerMode", "Trigger Mode", CameraTriggerMode.Continuous, "Trigger mode (HIK/Cognex)");
        _exposureTime = AddDoubleProperty("ExposureTime", "Exposure Time", 10000.0, 16.0, 10000000.0, "Exposure time in microseconds (HIK/Cognex)");
        _pixelFormat = AddEnumProperty("PixelFormat", "Pixel Format", CameraPixelFormat.Auto, "Pixel format (HIK/Cognex)");

        // HIK specific
        _gain = AddDoubleProperty("Gain", "Gain", 0.0, 0.0, 20.0, "Gain in dB (HIK)");
        _autoExposure = AddBoolProperty("AutoExposure", "Auto Exposure", false, "Auto exposure (HIK)");
        _autoGain = AddBoolProperty("AutoGain", "Auto Gain", false, "Auto gain (HIK)");
        _gammaEnable = AddBoolProperty("GammaEnable", "Gamma Enable", false, "Enable gamma correction (HIK)");
        _gamma = AddDoubleProperty("Gamma", "Gamma", 0.7, 0.1, 4.0, "Gamma value (HIK)");
        _reverseX = AddBoolProperty("ReverseX", "Reverse X", false, "Horizontal flip (HIK)");
        _reverseY = AddBoolProperty("ReverseY", "Reverse Y", false, "Vertical flip (HIK)");

        // Cognex specific
        _brightness = AddDoubleProperty("Brightness", "Brightness", 0.5, 0.0, 1.0, "Brightness (Cognex)");

        // Listen for device selection changes to update property visibility
        _deviceList.ValueChanged += UpdatePropertyVisibility;
        UpdatePropertyVisibility();
    }

    /// <summary>
    /// Show/hide properties based on the selected camera's backend type.
    /// USB → Width, Height, FPS, Backend
    /// HIK → Width, Height, TriggerMode, ExposureTime, PixelFormat, Gain, AutoExposure, AutoGain, Gamma*, ReverseX/Y
    /// Cognex → Width, Height, TriggerMode, ExposureTime, PixelFormat, Brightness
    /// No selection → show all properties
    /// </summary>
    private void UpdatePropertyVisibility()
    {
        var selectedIdx = _deviceList.GetValue<int>();
        string backendType = "";

        if (selectedIdx >= 0 && selectedIdx < _allDevices.Count)
            backendType = _allDevices[selectedIdx].BackendType;

        bool showAll = string.IsNullOrEmpty(backendType);
        bool isUsb = backendType == "USB" || showAll;
        bool isHik = backendType == "HIK" || showAll;
        bool isCognex = backendType == "Cognex" || showAll;
        bool isIndustrial = isHik || isCognex;

        // Common - always visible
        _width.SetVisible(true);
        _height.SetVisible(true);

        // USB specific
        _fps.SetVisible(isUsb);
        _usbBackend.SetVisible(isUsb);

        // Industrial (HIK/Cognex)
        _triggerMode.SetVisible(isIndustrial);
        _exposureTime.SetVisible(isIndustrial);
        _pixelFormat.SetVisible(isIndustrial);

        // HIK specific
        _gain.SetVisible(isHik);
        _autoExposure.SetVisible(isHik);
        _autoGain.SetVisible(isHik);
        _gammaEnable.SetVisible(isHik);
        _gamma.SetVisible(isHik);
        _reverseX.SetVisible(isHik);
        _reverseY.SetVisible(isHik);

        // Cognex specific
        _brightness.SetVisible(isCognex);
    }

    /// <summary>
    /// Enumerate all cameras from all available backends and merge into unified device list.
    /// </summary>
    public void EnumerateDevices()
    {
        _allDevices.Clear();

        // USB cameras (always available via OpenCvSharp)
        try
        {
            _usbNode ??= CreateBackend<UsbCameraNode>();
            if (_usbNode != null)
            {
                CallEnumerate(_usbNode);
                CollectDevices(_usbNode, "USB", "[USB] ");
            }
        }
        catch { }

        // HIK cameras (SDK may not be installed)
        try
        {
            _hikNode ??= CreateBackend<HikCameraNode>();
            if (_hikNode != null)
            {
                CallEnumerate(_hikNode);
                CollectDevices(_hikNode, "HIK", "");
            }
        }
        catch { }

        // Cognex cameras (SDK may not be installed)
        try
        {
            _cognexNode ??= CreateBackend<CognexGigECameraNode>();
            if (_cognexNode != null)
            {
                CallEnumerate(_cognexNode);
                CollectDevices(_cognexNode, "Cognex", "[Cognex] ");
            }
        }
        catch { }

        // Update the unified device list
        var options = _allDevices.Select(d => (d.DisplayName, d.GlobalIndex)).ToList();
        _deviceList.UpdateDeviceOptions(options);

        // Auto-select first device if none selected
        if (options.Count > 0 && _deviceList.GetValue<int>() < 0)
            _deviceList.SetValue(0);

        // Update property visibility for the current selection
        UpdatePropertyVisibility();
    }

    private static BaseNode? CreateBackend<T>() where T : BaseNode
    {
        try
        {
            return Activator.CreateInstance<T>();
        }
        catch
        {
            return null;
        }
    }

    private void CallEnumerate(BaseNode backendNode)
    {
        var method = backendNode.GetType().GetMethod("EnumerateDevices");
        method?.Invoke(backendNode, null);
    }

    private void CollectDevices(BaseNode backendNode, string backendType, string prefix)
    {
        var deviceListProp = backendNode.Properties.FirstOrDefault(p => p.Name == "DeviceList");
        if (deviceListProp == null) return;

        foreach (var (name, index) in deviceListProp.DeviceOptions)
        {
            var displayName = string.IsNullOrEmpty(prefix) ? name : prefix + name;
            _allDevices.Add(new DeviceEntry(displayName, _allDevices.Count, index, backendType));
        }
    }

    public override void Process()
    {
        try
        {
            var selectedIdx = _deviceList.GetValue<int>();
            if (selectedIdx < 0 || selectedIdx >= _allDevices.Count)
            {
                Error = "No camera selected";
                return;
            }

            var device = _allDevices[selectedIdx];
            var targetBackend = device.BackendType switch
            {
                "USB" => _usbNode,
                "HIK" => _hikNode,
                "Cognex" => _cognexNode,
                _ => null
            };

            if (targetBackend == null)
            {
                Error = $"Backend '{device.BackendType}' not available";
                return;
            }

            // Switch backend if camera type or device changed
            if (_activeBackend != targetBackend || _activeBackendIndex != device.BackendIndex)
            {
                // Cleanup previous backend
                if (_activeBackend != null && _activeBackend != targetBackend)
                    _activeBackend.Cleanup();

                _activeBackend = targetBackend;
                _activeBackendType = device.BackendType;
                _activeBackendIndex = device.BackendIndex;
            }

            // Forward device selection to backend
            SetBackendProperty(targetBackend, "DeviceList", device.BackendIndex);

            // Forward common properties
            SetBackendProperty(targetBackend, "Width", _width.GetValue<int>());
            SetBackendProperty(targetBackend, "Height", _height.GetValue<int>());

            // Forward backend-specific properties
            switch (device.BackendType)
            {
                case "USB":
                    SetBackendProperty(targetBackend, "FPS", _fps.GetValue<int>());
                    SetBackendProperty(targetBackend, "Backend", _usbBackend.GetValue<UsbCameraBackend>());
                    break;

                case "HIK":
                    ForwardHikProperties(targetBackend);
                    break;

                case "Cognex":
                    ForwardCognexProperties(targetBackend);
                    break;
            }

            // Forward runtime mode
            targetBackend.IsRuntimeMode = IsRuntimeMode;

            // Execute backend capture
            targetBackend.Process();

            // Read frame from backend's output port
            var framePort = targetBackend.Outputs.FirstOrDefault(o => o.Name == "Frame");
            var frame = framePort?.GetValue() as Mat;

            if (frame != null && !frame.Empty())
            {
                SetOutputValue(_frameOutput, frame.Clone());
                SetPreview(frame);
                Error = null;
            }
            else
            {
                Error = targetBackend.Error ?? "No frame captured";
            }
        }
        catch (Exception ex)
        {
            Error = $"Camera error: {ex.Message}";
        }
    }

    private void ForwardHikProperties(BaseNode backend)
    {
        // Map unified trigger mode → HIK trigger mode
        var triggerMode = _triggerMode.GetValue<CameraTriggerMode>();
        var hikTrigger = triggerMode switch
        {
            CameraTriggerMode.Continuous => HikTriggerMode.Continuous,
            CameraTriggerMode.Software => HikTriggerMode.Software,
            CameraTriggerMode.Hardware => HikTriggerMode.Hardware,
            _ => HikTriggerMode.Continuous
        };
        SetBackendProperty(backend, "TriggerMode", hikTrigger);

        // Map unified pixel format → HIK pixel format
        var pixelFormat = _pixelFormat.GetValue<CameraPixelFormat>();
        var hikPixel = pixelFormat switch
        {
            CameraPixelFormat.Mono8 => HikPixelFormat.Mono8,
            CameraPixelFormat.BayerRG8 => HikPixelFormat.BayerRG8,
            CameraPixelFormat.BayerGR8 => HikPixelFormat.BayerGR8,
            CameraPixelFormat.BayerBG8 => HikPixelFormat.BayerBG8,
            CameraPixelFormat.BayerGB8 => HikPixelFormat.BayerGB8,
            CameraPixelFormat.RGB8 => HikPixelFormat.RGB8,
            CameraPixelFormat.BGR8 => HikPixelFormat.BGR8,
            CameraPixelFormat.YUV422_8 => HikPixelFormat.YUV422_8,
            _ => HikPixelFormat.Mono8
        };
        if (pixelFormat != CameraPixelFormat.Auto)
            SetBackendProperty(backend, "PixelFormat", hikPixel);

        SetBackendProperty(backend, "ExposureTime", _exposureTime.GetValue<double>());
        SetBackendProperty(backend, "Gain", _gain.GetValue<double>());
        SetBackendProperty(backend, "AutoExposure", _autoExposure.GetValue<bool>());
        SetBackendProperty(backend, "AutoGain", _autoGain.GetValue<bool>());
        SetBackendProperty(backend, "GammaEnable", _gammaEnable.GetValue<bool>());
        SetBackendProperty(backend, "Gamma", _gamma.GetValue<double>());
        SetBackendProperty(backend, "ReverseX", _reverseX.GetValue<bool>());
        SetBackendProperty(backend, "ReverseY", _reverseY.GetValue<bool>());

        // Note: Trigger input forwarding is handled at the SDK level by the backend node.
        // Software trigger requires direct SDK calls which the backend handles in its Process().
    }

    private void ForwardCognexProperties(BaseNode backend)
    {
        // Map unified trigger mode → Cognex trigger mode
        var triggerMode = _triggerMode.GetValue<CameraTriggerMode>();
        var cogTrigger = triggerMode switch
        {
            CameraTriggerMode.Continuous => CognexTriggerMode.FreeRun,
            CameraTriggerMode.Software => CognexTriggerMode.Software,
            CameraTriggerMode.Hardware => CognexTriggerMode.Hardware,
            _ => CognexTriggerMode.FreeRun
        };
        SetBackendProperty(backend, "TriggerMode", cogTrigger);

        // Map unified pixel format → Cognex pixel format
        var pixelFormat = _pixelFormat.GetValue<CameraPixelFormat>();
        var cogPixel = pixelFormat switch
        {
            CameraPixelFormat.Mono8 => CognexPixelFormat.Mono8,
            CameraPixelFormat.Color24 => CognexPixelFormat.Color24,
            CameraPixelFormat.RGB8 => CognexPixelFormat.Color24,
            _ => CognexPixelFormat.Mono8
        };
        if (pixelFormat != CameraPixelFormat.Auto)
            SetBackendProperty(backend, "PixelFormat", cogPixel);

        SetBackendProperty(backend, "ExposureTime", _exposureTime.GetValue<double>());
        SetBackendProperty(backend, "Brightness", _brightness.GetValue<double>());
    }

    private static void SetBackendProperty(BaseNode backend, string name, object? value)
    {
        var prop = backend.Properties.FirstOrDefault(p => p.Name == name);
        if (prop != null && value != null)
        {
            try { prop.SetValue(value); }
            catch { }
        }
    }

    public override void Cleanup()
    {
        _activeBackend?.Cleanup();
        _activeBackend = null;
        _activeBackendType = "";
        _activeBackendIndex = -1;

        // Cleanup all backend instances
        (_usbNode as BaseNode)?.Cleanup();
        (_hikNode as BaseNode)?.Cleanup();
        (_cognexNode as BaseNode)?.Cleanup();

        base.Cleanup();
    }
}
