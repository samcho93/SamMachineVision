using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using System.Runtime.InteropServices;

namespace MVXTester.Nodes.Input;

public enum HikTriggerMode
{
    Continuous,
    Software,
    Hardware
}

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

    private object? _camera;
    private bool _isOpen;
    private int _lastDeviceIndex = -1;

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
    }

    public override void Process()
    {
        try
        {
            var deviceIndex = _deviceIndex.GetValue<int>();

            if (!_isOpen || deviceIndex != _lastDeviceIndex)
            {
                CloseCamera();
                OpenCamera(deviceIndex);
                _lastDeviceIndex = deviceIndex;
            }

            if (!_isOpen || _camera == null)
            {
                Error = "Camera not opened";
                return;
            }

            var cameraType = _camera.GetType();
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();

            // Set exposure
            var exposureTime = _exposureTime.GetValue<double>();
            cameraType.GetMethod("MV_CC_SetFloatValue")?.Invoke(_camera, new object[] { "ExposureTime", (float)exposureTime });

            // Set gain
            var gain = _gain.GetValue<double>();
            cameraType.GetMethod("MV_CC_SetFloatValue")?.Invoke(_camera, new object[] { "Gain", (float)gain });

            // Software trigger
            if (triggerMode == HikTriggerMode.Software)
            {
                cameraType.GetMethod("MV_CC_SetCommandValue")?.Invoke(_camera, new object[] { "TriggerSoftware" });
            }

            // Get one frame
            var getImageMethod = cameraType.GetMethod("MV_CC_GetOneFrameTimeout");
            if (getImageMethod == null)
            {
                Error = "MV_CC_GetOneFrameTimeout method not found";
                return;
            }

            // Allocate buffer
            int bufferSize = 4096 * 3072 * 3;
            byte[] buffer = new byte[bufferSize];
            var frameInfoType = cameraType.Assembly.GetType("MvCamCtrl.NET.MyCamera+MV_FRAME_OUT_INFO_EX");
            if (frameInfoType == null)
            {
                Error = "Frame info type not found";
                return;
            }

            var frameInfo = Activator.CreateInstance(frameInfoType);
            var args = new object[] { buffer, (uint)bufferSize, frameInfo!, 1000u };
            var ret = (int)(getImageMethod.Invoke(_camera, args) ?? -1);

            if (ret != 0)
            {
                Error = $"Get frame failed: 0x{ret:X8}";
                return;
            }

            frameInfo = args[2];
            int w = (int)(ushort)(frameInfoType.GetField("nWidth")?.GetValue(frameInfo) ?? 0);
            int h = (int)(ushort)(frameInfoType.GetField("nHeight")?.GetValue(frameInfo) ?? 0);

            if (w <= 0 || h <= 0)
            {
                Error = "Invalid frame dimensions";
                return;
            }

            // Determine pixel format
            var pixelType = frameInfoType.GetField("enPixelType")?.GetValue(frameInfo);
            int pixelTypeInt = pixelType != null ? (int)Convert.ChangeType(pixelType, typeof(int)) : 0;
            bool isMono = (pixelTypeInt & 0x01000000) != 0;

            Mat frame;
            if (isMono)
            {
                frame = new Mat(h, w, MatType.CV_8UC1);
                Marshal.Copy(buffer, 0, frame.Data, w * h);
            }
            else
            {
                frame = new Mat(h, w, MatType.CV_8UC3);
                Marshal.Copy(buffer, 0, frame.Data, w * h * 3);
            }

            SetOutputValue(_frameOutput, frame);
            SetPreview(frame);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"HIK Camera error: {ex.Message}";
        }
    }

    private void OpenCamera(int deviceIndex)
    {
        try
        {
            var mvCameraType = Type.GetType("MvCamCtrl.NET.MyCamera, MvCameraControl.Net");
            if (mvCameraType == null)
            {
                Error = "MvCameraControl.Net SDK not available";
                return;
            }

            // Enumerate devices
            var deviceListType = mvCameraType.Assembly.GetType("MvCamCtrl.NET.MyCamera+MV_CC_DEVICE_INFO_LIST");
            if (deviceListType == null)
            {
                Error = "Device list type not found";
                return;
            }

            var deviceList = Activator.CreateInstance(deviceListType);
            var enumMethod = mvCameraType.GetMethod("MV_CC_EnumDevices_NET", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (enumMethod == null)
            {
                Error = "Enum devices method not found";
                return;
            }

            // MV_GIGE_DEVICE | MV_USB_DEVICE = 0x1 | 0x4
            var ret = (int)(enumMethod.Invoke(null, new object[] { 0x1 | 0x4, deviceList! }) ?? -1);
            if (ret != 0)
            {
                Error = $"Enumerate devices failed: 0x{ret:X8}";
                return;
            }

            var deviceCountField = deviceListType.GetField("nDeviceNum");
            uint deviceCount = (uint)(deviceCountField?.GetValue(deviceList) ?? 0);
            if (deviceCount == 0 || deviceIndex >= deviceCount)
            {
                Error = $"No camera found at index {deviceIndex} (found {deviceCount})";
                return;
            }

            _camera = Activator.CreateInstance(mvCameraType);
            if (_camera == null)
            {
                Error = "Failed to create camera instance";
                return;
            }

            var camType = _camera.GetType();

            // Get device info from list
            var deviceInfoArrayField = deviceListType.GetField("pDeviceInfo");
            var deviceInfoArray = deviceInfoArrayField?.GetValue(deviceList) as Array;
            var deviceInfo = deviceInfoArray?.GetValue(deviceIndex);

            // Create handle
            var createMethod = camType.GetMethod("MV_CC_CreateDevice");
            if (createMethod != null && deviceInfo != null)
            {
                ret = (int)(createMethod.Invoke(_camera, new object[] { deviceInfo }) ?? -1);
                if (ret != 0)
                {
                    Error = $"Create device failed: 0x{ret:X8}";
                    return;
                }
            }

            // Open device
            var openMethod = camType.GetMethod("MV_CC_OpenDevice");
            if (openMethod != null)
            {
                ret = (int)(openMethod.Invoke(_camera, new object[] { 0, 0 }) ?? -1);
                if (ret != 0)
                {
                    Error = $"Open device failed: 0x{ret:X8}";
                    return;
                }
            }

            // Set trigger mode
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();
            if (triggerMode == HikTriggerMode.Continuous)
            {
                camType.GetMethod("MV_CC_SetEnumValue")?.Invoke(_camera, new object[] { "TriggerMode", 0u });
            }
            else
            {
                camType.GetMethod("MV_CC_SetEnumValue")?.Invoke(_camera, new object[] { "TriggerMode", 1u });
                if (triggerMode == HikTriggerMode.Software)
                    camType.GetMethod("MV_CC_SetEnumValue")?.Invoke(_camera, new object[] { "TriggerSource", 7u });
                else
                    camType.GetMethod("MV_CC_SetEnumValue")?.Invoke(_camera, new object[] { "TriggerSource", 0u });
            }

            // Set ROI if specified
            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            if (width > 0)
                camType.GetMethod("MV_CC_SetIntValue")?.Invoke(_camera, new object[] { "Width", (uint)width });
            if (height > 0)
                camType.GetMethod("MV_CC_SetIntValue")?.Invoke(_camera, new object[] { "Height", (uint)height });

            // Start grabbing
            var startMethod = camType.GetMethod("MV_CC_StartGrabbing");
            if (startMethod != null)
            {
                ret = (int)(startMethod.Invoke(_camera, null) ?? -1);
                if (ret != 0)
                {
                    Error = $"Start grabbing failed: 0x{ret:X8}";
                    return;
                }
            }

            _isOpen = true;
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Failed to open HIK camera: {ex.Message}";
            _isOpen = false;
        }
    }

    private void CloseCamera()
    {
        try
        {
            if (_camera != null && _isOpen)
            {
                var camType = _camera.GetType();
                camType.GetMethod("MV_CC_StopGrabbing")?.Invoke(_camera, null);
                camType.GetMethod("MV_CC_CloseDevice")?.Invoke(_camera, null);
                camType.GetMethod("MV_CC_DestroyDevice")?.Invoke(_camera, null);
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
