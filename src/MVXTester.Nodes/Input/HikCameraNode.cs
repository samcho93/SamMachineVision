using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MVXTester.Nodes.Input;

public enum HikTriggerMode
{
    Continuous,
    Software,
    Hardware
}

public enum HikPixelFormat
{
    Mono8,
    BayerRG8,
    RGB8,
    BGR8,
    YUV422_8
}

/// <summary>
/// HIK Camera node using dynamic assembly loading to avoid startup crash
/// when MvCameraControl.Net.dll (.NET Framework 4.x) is referenced from .NET 8.
/// </summary>
[NodeInfo("HIK Camera", NodeCategories.Input, Description = "HIK GigE camera capture using MvCameraControl.Net SDK")]
public class HikCameraNode : BaseNode, IStreamingSource
{
    private InputPort<int> _triggerInput = null!;
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _deviceIndex = null!;
    private NodeProperty _triggerMode = null!;
    private NodeProperty _exposureTime = null!;
    private NodeProperty _gain = null!;
    private NodeProperty _width = null!;
    private NodeProperty _height = null!;
    private NodeProperty _pixelFormat = null!;
    private NodeProperty _gamma = null!;
    private NodeProperty _gammaEnable = null!;
    private NodeProperty _autoExposure = null!;
    private NodeProperty _autoGain = null!;
    private NodeProperty _reverseX = null!;
    private NodeProperty _reverseY = null!;

    private object? _camera; // MyCamera instance
    private Type? _myCameraType;
    private Assembly? _sdkAssembly;
    private bool _isOpen;
    private int _lastDeviceIndex = -1;
    private int _lastTriggerValue;
    private HikPixelFormat _lastPixelFormat = HikPixelFormat.Mono8;

    // Cached method references
    private MethodInfo? _setFloatValue;
    private MethodInfo? _setEnumValue;
    private MethodInfo? _setIntValueEx;
    private MethodInfo? _setCommandValue;
    private MethodInfo? _setBoolValue;
    private MethodInfo? _getImageBuffer;
    private MethodInfo? _freeImageBuffer;
    private MethodInfo? _startGrabbing;
    private MethodInfo? _stopGrabbing;
    private MethodInfo? _closeDevice;
    private MethodInfo? _destroyDevice;
    private MethodInfo? _getOptimalPacketSize;

    // Cached type/field references
    private Type? _frameOutType;
    private Type? _frameInfoType;
    private FieldInfo? _frameOut_stFrameInfo;
    private FieldInfo? _frameOut_pBufAddr;
    private FieldInfo? _frameInfo_nWidth;
    private FieldInfo? _frameInfo_nHeight;
    private FieldInfo? _frameInfo_nFrameLen;
    private FieldInfo? _frameInfo_enPixelType;

    private int _mvOk;

    protected override void Setup()
    {
        _triggerInput = AddInput<int>("Trigger");
        _frameOutput = AddOutput<Mat>("Frame");
        _deviceIndex = AddIntProperty("DeviceIndex", "Device Index", 0, 0, 16, "Camera device index");
        _triggerMode = AddEnumProperty("TriggerMode", "Trigger Mode", HikTriggerMode.Continuous, "Trigger mode");
        _exposureTime = AddDoubleProperty("ExposureTime", "Exposure Time (us)", 10000.0, 16.0, 10000000.0, "Exposure time in microseconds");
        _gain = AddDoubleProperty("Gain", "Gain (dB)", 0.0, 0.0, 20.0, "Analog gain in dB");
        _width = AddIntProperty("Width", "Width", 0, 0, 10000, "Image width (0 = max)");
        _height = AddIntProperty("Height", "Height", 0, 0, 10000, "Image height (0 = max)");
        _pixelFormat = AddEnumProperty("PixelFormat", "Pixel Format", HikPixelFormat.Mono8, "Camera pixel format");
        _gammaEnable = AddBoolProperty("GammaEnable", "Gamma Enable", false, "Enable gamma correction");
        _gamma = AddDoubleProperty("Gamma", "Gamma", 0.7, 0.1, 4.0, "Gamma value (0.1~4.0)");
        _autoExposure = AddBoolProperty("AutoExposure", "Auto Exposure", false, "Enable auto exposure");
        _autoGain = AddBoolProperty("AutoGain", "Auto Gain", false, "Enable auto gain");
        _reverseX = AddBoolProperty("ReverseX", "Reverse X", false, "Flip image horizontally");
        _reverseY = AddBoolProperty("ReverseY", "Reverse Y", false, "Flip image vertically");
    }

    public override void Process()
    {
        try
        {
            var deviceIndex = _deviceIndex.GetValue<int>();
            var pixelFormat = _pixelFormat.GetValue<HikPixelFormat>();

            if (!_isOpen || deviceIndex != _lastDeviceIndex || pixelFormat != _lastPixelFormat)
            {
                CloseCamera();
                OpenCamera(deviceIndex);
                _lastDeviceIndex = deviceIndex;
                _lastPixelFormat = pixelFormat;
            }

            if (!_isOpen || _camera == null)
                return; // Error already set in OpenCamera

            // Auto exposure
            var autoExposure = _autoExposure.GetValue<bool>();
            _setEnumValue?.Invoke(_camera, new object[] { "ExposureAuto", autoExposure ? 2u : 0u });

            // Set exposure (only when manual)
            if (!autoExposure)
            {
                var exposureTime = _exposureTime.GetValue<double>();
                _setFloatValue?.Invoke(_camera, new object[] { "ExposureTime", (float)exposureTime });
            }

            // Auto gain
            var autoGain = _autoGain.GetValue<bool>();
            _setEnumValue?.Invoke(_camera, new object[] { "GainAuto", autoGain ? 2u : 0u });

            // Set gain (only when manual)
            if (!autoGain)
            {
                var gain = _gain.GetValue<double>();
                _setFloatValue?.Invoke(_camera, new object[] { "Gain", (float)gain });
            }

            // Gamma
            var gammaEnable = _gammaEnable.GetValue<bool>();
            _setBoolValue?.Invoke(_camera, new object[] { "GammaEnable", gammaEnable });
            if (gammaEnable)
            {
                var gamma = _gamma.GetValue<double>();
                _setFloatValue?.Invoke(_camera, new object[] { "Gamma", (float)gamma });
            }

            // Reverse
            _setBoolValue?.Invoke(_camera, new object[] { "ReverseX", _reverseX.GetValue<bool>() });
            _setBoolValue?.Invoke(_camera, new object[] { "ReverseY", _reverseY.GetValue<bool>() });

            // Software trigger
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();
            if (triggerMode == HikTriggerMode.Software)
            {
                var triggerVal = GetInputValue(_triggerInput);
                if (triggerVal != _lastTriggerValue || _triggerInput.Connection == null)
                {
                    _setCommandValue?.Invoke(_camera, new object[] { "TriggerSoftware" });
                    _lastTriggerValue = triggerVal;
                }
            }

            // Get frame - MV_CC_GetImageBuffer_NET(ref MV_FRAME_OUT, int timeout)
            var frameOut = Activator.CreateInstance(_frameOutType!)!;
            var getArgs = new object[] { frameOut, 1000 };
            int ret = (int)(_getImageBuffer?.Invoke(_camera, getArgs) ?? -1);

            if (ret != _mvOk)
            {
                Error = $"Get frame failed: 0x{ret:X8}";
                return;
            }

            // Read back ref parameter
            frameOut = getArgs[0];

            try
            {
                var stFrameInfo = _frameOut_stFrameInfo?.GetValue(frameOut);
                if (stFrameInfo == null)
                {
                    Error = "Cannot read frame info";
                    return;
                }

                uint w = Convert.ToUInt32(_frameInfo_nWidth?.GetValue(stFrameInfo) ?? 0);
                uint h = Convert.ToUInt32(_frameInfo_nHeight?.GetValue(stFrameInfo) ?? 0);
                uint frameLen = Convert.ToUInt32(_frameInfo_nFrameLen?.GetValue(stFrameInfo) ?? 0);
                var pBufAddrObj = _frameOut_pBufAddr?.GetValue(frameOut);
                IntPtr pBufAddr = pBufAddrObj is IntPtr ptr ? ptr : IntPtr.Zero;

                if (w == 0 || h == 0 || pBufAddr == IntPtr.Zero)
                {
                    Error = "Invalid frame data";
                    return;
                }

                // Determine pixel format from actual frame data
                var pixelType = _frameInfo_enPixelType?.GetValue(stFrameInfo);
                int pxType = pixelType != null ? Convert.ToInt32(pixelType) : 0;

                // GigE Vision pixel format constants
                const int PX_MONO8     = 0x01080001;
                const int PX_MONO10    = 0x01100003;
                const int PX_MONO12    = 0x01100005;
                const int PX_BAYER_RG8 = 0x01080009;
                const int PX_BAYER_BG8 = 0x0108000A;
                const int PX_BAYER_GB8 = 0x0108000B;
                const int PX_BAYER_GR8 = 0x01080010;
                const int PX_RGB8      = 0x02180014;
                const int PX_BGR8      = 0x02180015;
                // const int PX_YUV422_8  = 0x02100032;

                Mat frame;
                int iw = (int)w, ih = (int)h;

                switch (pxType)
                {
                    case PX_MONO8:
                    case PX_MONO10:
                    case PX_MONO12:
                    {
                        // True mono - single channel grayscale
                        int size = iw * ih;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        frame = new Mat(ih, iw, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, frame.Data, size);
                        break;
                    }
                    case PX_BAYER_RG8:
                    {
                        int size = iw * ih;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        var bayer = new Mat(ih, iw, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, bayer.Data, size);
                        frame = new Mat();
                        Cv2.CvtColor(bayer, frame, ColorConversionCodes.BayerRG2BGR);
                        bayer.Dispose();
                        break;
                    }
                    case PX_BAYER_BG8:
                    {
                        int size = iw * ih;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        var bayer = new Mat(ih, iw, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, bayer.Data, size);
                        frame = new Mat();
                        Cv2.CvtColor(bayer, frame, ColorConversionCodes.BayerBG2BGR);
                        bayer.Dispose();
                        break;
                    }
                    case PX_BAYER_GR8:
                    {
                        int size = iw * ih;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        var bayer = new Mat(ih, iw, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, bayer.Data, size);
                        frame = new Mat();
                        Cv2.CvtColor(bayer, frame, ColorConversionCodes.BayerGR2BGR);
                        bayer.Dispose();
                        break;
                    }
                    case PX_BAYER_GB8:
                    {
                        int size = iw * ih;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        var bayer = new Mat(ih, iw, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, bayer.Data, size);
                        frame = new Mat();
                        Cv2.CvtColor(bayer, frame, ColorConversionCodes.BayerGB2BGR);
                        bayer.Dispose();
                        break;
                    }
                    case PX_RGB8:
                    {
                        int size = iw * ih * 3;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        frame = new Mat(ih, iw, MatType.CV_8UC3);
                        Marshal.Copy(data, 0, frame.Data, size);
                        Cv2.CvtColor(frame, frame, ColorConversionCodes.RGB2BGR);
                        break;
                    }
                    case PX_BGR8:
                    {
                        int size = iw * ih * 3;
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        frame = new Mat(ih, iw, MatType.CV_8UC3);
                        Marshal.Copy(data, 0, frame.Data, size);
                        break;
                    }
                    default:
                    {
                        // Fallback: guess by frame length
                        int monoSize = iw * ih;
                        int rgbSize = iw * ih * 3;
                        if (frameLen >= (uint)rgbSize)
                        {
                            byte[] data = new byte[rgbSize];
                            Marshal.Copy(pBufAddr, data, 0, rgbSize);
                            frame = new Mat(ih, iw, MatType.CV_8UC3);
                            Marshal.Copy(data, 0, frame.Data, rgbSize);
                            Cv2.CvtColor(frame, frame, ColorConversionCodes.RGB2BGR);
                        }
                        else
                        {
                            byte[] data = new byte[monoSize];
                            Marshal.Copy(pBufAddr, data, 0, monoSize);
                            frame = new Mat(ih, iw, MatType.CV_8UC1);
                            Marshal.Copy(data, 0, frame.Data, monoSize);
                        }
                        break;
                    }
                }

                SetOutputValue(_frameOutput, frame);
                SetPreview(frame);
                Error = null;
            }
            finally
            {
                // Free buffer - MV_CC_FreeImageBuffer_NET(ref MV_FRAME_OUT)
                var freeArgs = new object[] { frameOut };
                _freeImageBuffer?.Invoke(_camera, freeArgs);
            }
        }
        catch (Exception ex)
        {
            Error = $"HIK Camera error: {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    private bool LoadSdk()
    {
        if (_sdkAssembly != null) return true;

        try
        {
            // Try loading from app directory first
            var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MvCameraControl.Net.dll");
            if (!File.Exists(dllPath))
            {
                // Try MVS SDK installation paths
                var sdkPaths = new[]
                {
                    @"C:\Program Files (x86)\MVS\Development\DotNet\AnyCpu\MvCameraControl.Net.dll",
                    @"C:\Program Files (x86)\MVS\Development\DotNet\win64\MvCameraControl.Net.dll",
                    @"C:\Program Files\MVS\Development\DotNet\AnyCpu\MvCameraControl.Net.dll",
                };
                dllPath = sdkPaths.FirstOrDefault(File.Exists) ?? dllPath;
            }

            if (!File.Exists(dllPath))
            {
                Error = $"MvCameraControl.Net.dll not found at {dllPath}";
                return false;
            }

            _sdkAssembly = Assembly.LoadFrom(dllPath);
            _myCameraType = _sdkAssembly.GetType("MvCamCtrl.NET.MyCamera");
            if (_myCameraType == null)
            {
                Error = "MyCamera type not found in SDK assembly";
                return false;
            }

            // Cache MV_OK constant
            var mvOkField = _myCameraType.GetField("MV_OK", BindingFlags.Static | BindingFlags.Public);
            _mvOk = mvOkField != null ? (int)mvOkField.GetValue(null)! : 0;

            // Cache frame types and fields
            _frameOutType = _sdkAssembly.GetType("MvCamCtrl.NET.MyCamera+MV_FRAME_OUT");
            if (_frameOutType != null)
            {
                _frameOut_stFrameInfo = _frameOutType.GetField("stFrameInfo");
                _frameOut_pBufAddr = _frameOutType.GetField("pBufAddr");
            }

            var frameInfoExType = _sdkAssembly.GetType("MvCamCtrl.NET.MyCamera+MV_FRAME_OUT_INFO_EX");
            if (frameInfoExType != null)
            {
                _frameInfo_nWidth = frameInfoExType.GetField("nWidth");
                _frameInfo_nHeight = frameInfoExType.GetField("nHeight");
                _frameInfo_nFrameLen = frameInfoExType.GetField("nFrameLen");
                _frameInfo_enPixelType = frameInfoExType.GetField("enPixelType");
                _frameInfoType = frameInfoExType;
            }

            return true;
        }
        catch (Exception ex)
        {
            Error = $"Failed to load HIK SDK: {ex.Message}";
            return false;
        }
    }

    private void CacheMethodReferences()
    {
        if (_camera == null) return;
        var camType = _camera.GetType();

        // Use GetMethods + filter to avoid AmbiguousMatchException on overloaded methods
        MethodInfo? FindMethod(string name, int paramCount)
        {
            return camType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == paramCount);
        }

        _setFloatValue = FindMethod("MV_CC_SetFloatValue_NET", 2);         // (string, float)
        _setEnumValue = FindMethod("MV_CC_SetEnumValue_NET", 2);           // (string, uint)
        _setIntValueEx = FindMethod("MV_CC_SetIntValueEx_NET", 2);         // (string, long)
        _setCommandValue = FindMethod("MV_CC_SetCommandValue_NET", 1);     // (string)
        _setBoolValue = FindMethod("MV_CC_SetBoolValue_NET", 2);           // (string, bool)
        _getImageBuffer = FindMethod("MV_CC_GetImageBuffer_NET", 2);       // (ref MV_FRAME_OUT, int)
        _freeImageBuffer = FindMethod("MV_CC_FreeImageBuffer_NET", 1);     // (ref MV_FRAME_OUT)
        _startGrabbing = FindMethod("MV_CC_StartGrabbing_NET", 0);         // ()
        _stopGrabbing = FindMethod("MV_CC_StopGrabbing_NET", 0);           // ()
        _closeDevice = FindMethod("MV_CC_CloseDevice_NET", 0);             // ()
        _destroyDevice = FindMethod("MV_CC_DestroyDevice_NET", 0);         // ()
        _getOptimalPacketSize = FindMethod("MV_CC_GetOptimalPacketSize_NET", 0); // ()
    }

    private void OpenCamera(int deviceIndex)
    {
        try
        {
            if (!LoadSdk()) return;

            // Initialize SDK
            var initMethod = _myCameraType!.GetMethod("MV_CC_Initialize_NET",
                BindingFlags.Static | BindingFlags.Public);
            initMethod?.Invoke(null, null);

            // Enumerate devices
            var deviceListType = _sdkAssembly!.GetType("MvCamCtrl.NET.MyCamera+MV_CC_DEVICE_INFO_LIST");
            if (deviceListType == null) { Error = "Device list type not found"; return; }

            var deviceList = Activator.CreateInstance(deviceListType)!;

            // Get device type constants
            var gigeField = _myCameraType.GetField("MV_GIGE_DEVICE", BindingFlags.Static | BindingFlags.Public);
            var usbField = _myCameraType.GetField("MV_USB_DEVICE", BindingFlags.Static | BindingFlags.Public);
            uint deviceFlags = 0x1 | 0x4; // defaults
            if (gigeField != null && usbField != null)
                deviceFlags = Convert.ToUInt32(gigeField.GetValue(null)) | Convert.ToUInt32(usbField.GetValue(null));

            var enumMethod = _myCameraType.GetMethods(BindingFlags.Static | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "MV_CC_EnumDevices_NET" && m.GetParameters().Length == 2);
            if (enumMethod == null) { Error = "EnumDevices method not found"; return; }

            // Call with ref: args array captures modified ref value
            var enumArgs = new object[] { deviceFlags, deviceList };
            int ret = (int)(enumMethod.Invoke(null, enumArgs) ?? -1);
            deviceList = enumArgs[1]; // Read back ref parameter

            if (ret != _mvOk) { Error = $"Enumerate failed: 0x{ret:X8}"; return; }

            var nDeviceNum = Convert.ToUInt32(deviceListType.GetField("nDeviceNum")?.GetValue(deviceList) ?? 0);
            if (nDeviceNum == 0) { Error = "No HIK cameras found"; return; }
            if (deviceIndex >= (int)nDeviceNum)
            {
                Error = $"Index {deviceIndex} out of range ({nDeviceNum} found)";
                return;
            }

            // Get device info via Marshal.PtrToStructure
            var deviceInfoType = _sdkAssembly.GetType("MvCamCtrl.NET.MyCamera+MV_CC_DEVICE_INFO");
            if (deviceInfoType == null) { Error = "DeviceInfo type not found"; return; }

            var pDeviceInfo = deviceListType.GetField("pDeviceInfo")?.GetValue(deviceList) as IntPtr[];
            if (pDeviceInfo == null || pDeviceInfo.Length <= deviceIndex)
            {
                Error = "Failed to get device info";
                return;
            }

            var deviceInfo = Marshal.PtrToStructure(pDeviceInfo[deviceIndex], deviceInfoType)!;

            // Create camera instance
            _camera = Activator.CreateInstance(_myCameraType);
            if (_camera == null) { Error = "Failed to create camera"; return; }
            CacheMethodReferences();

            // Create device (ref parameter) - use param count to avoid ambiguous match
            var camType = _camera.GetType();
            var createMethod = camType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "MV_CC_CreateDevice_NET" && m.GetParameters().Length == 1);
            if (createMethod != null)
            {
                var createArgs = new object[] { deviceInfo };
                ret = (int)(createMethod.Invoke(_camera, createArgs) ?? -1);
                if (ret != _mvOk) { Error = $"CreateDevice failed: 0x{ret:X8}"; _camera = null; return; }
            }

            // Open device - parameterless overload
            var openMethod = camType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "MV_CC_OpenDevice_NET" && m.GetParameters().Length == 0);
            if (openMethod != null)
            {
                ret = (int)(openMethod.Invoke(_camera, null) ?? -1);
                if (ret != _mvOk)
                {
                    Error = $"Open camera failed: 0x{ret:X8}";
                    _destroyDevice?.Invoke(_camera, null);
                    _camera = null;
                    return;
                }
            }

            // Set optimal packet size for GigE
            var nTLayerType = Convert.ToUInt32(deviceInfoType.GetField("nTLayerType")?.GetValue(deviceInfo) ?? 0);
            if (nTLayerType == 0x1 && _getOptimalPacketSize != null) // MV_GIGE_DEVICE
            {
                int packetSize = (int)(_getOptimalPacketSize.Invoke(_camera, null) ?? 0);
                if (packetSize > 0)
                    _setIntValueEx?.Invoke(_camera, new object[] { "GevSCPSPacketSize", (long)packetSize });
            }

            // Set trigger mode
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();
            if (triggerMode == HikTriggerMode.Continuous)
            {
                _setEnumValue?.Invoke(_camera, new object[] { "TriggerMode", 0u });
            }
            else
            {
                _setEnumValue?.Invoke(_camera, new object[] { "TriggerMode", 1u });
                _setEnumValue?.Invoke(_camera, new object[] { "TriggerSource",
                    triggerMode == HikTriggerMode.Software ? 7u : 0u });
            }

            // Set ROI
            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            if (width > 0)
                _setIntValueEx?.Invoke(_camera, new object[] { "Width", (long)width });
            if (height > 0)
                _setIntValueEx?.Invoke(_camera, new object[] { "Height", (long)height });

            // Set pixel format (must be set before grabbing)
            var pixelFormat = _pixelFormat.GetValue<HikPixelFormat>();
            uint pixelFormatValue = pixelFormat switch
            {
                HikPixelFormat.Mono8 => 0x01080001,
                HikPixelFormat.BayerRG8 => 0x01080009,
                HikPixelFormat.RGB8 => 0x02180014,
                HikPixelFormat.BGR8 => 0x02180015,
                HikPixelFormat.YUV422_8 => 0x02100032,
                _ => 0x01080001
            };
            _setEnumValue?.Invoke(_camera, new object[] { "PixelFormat", pixelFormatValue });

            // Set initial params
            _setFloatValue?.Invoke(_camera, new object[] { "ExposureTime", (float)_exposureTime.GetValue<double>() });
            _setFloatValue?.Invoke(_camera, new object[] { "Gain", (float)_gain.GetValue<double>() });

            // Set reverse
            _setBoolValue?.Invoke(_camera, new object[] { "ReverseX", _reverseX.GetValue<bool>() });
            _setBoolValue?.Invoke(_camera, new object[] { "ReverseY", _reverseY.GetValue<bool>() });

            // Set gamma
            var gammaEnable = _gammaEnable.GetValue<bool>();
            _setBoolValue?.Invoke(_camera, new object[] { "GammaEnable", gammaEnable });
            if (gammaEnable)
                _setFloatValue?.Invoke(_camera, new object[] { "Gamma", (float)_gamma.GetValue<double>() });

            // Start grabbing
            ret = (int)(_startGrabbing?.Invoke(_camera, null) ?? -1);
            if (ret != _mvOk)
            {
                Error = $"StartGrabbing failed: 0x{ret:X8}";
                _closeDevice?.Invoke(_camera, null);
                _destroyDevice?.Invoke(_camera, null);
                _camera = null;
                return;
            }

            _isOpen = true;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Open camera failed: {ex.InnerException?.Message ?? ex.Message}";
            _isOpen = false;
        }
    }

    private void CloseCamera()
    {
        try
        {
            if (_camera != null && _isOpen)
            {
                _stopGrabbing?.Invoke(_camera, null);
                _closeDevice?.Invoke(_camera, null);
                _destroyDevice?.Invoke(_camera, null);
            }
        }
        catch { }
        finally
        {
            _camera = null;
            _isOpen = false;
        }
    }

    public override void Cleanup()
    {
        CloseCamera();
        base.Cleanup();
    }
}
