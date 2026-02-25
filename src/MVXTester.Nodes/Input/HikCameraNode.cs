using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using System.Runtime.InteropServices;
using MvCamCtrl.NET;

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

    private MyCamera? _camera;
    private bool _isOpen;
    private int _lastDeviceIndex = -1;
    private int _lastTriggerValue;
    private static bool _sdkInitialized;

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

            // Set exposure
            var exposureTime = _exposureTime.GetValue<double>();
            _camera.MV_CC_SetFloatValue_NET("ExposureTime", (float)exposureTime);

            // Set gain
            var gain = _gain.GetValue<double>();
            _camera.MV_CC_SetFloatValue_NET("Gain", (float)gain);

            // Software trigger - only fire when trigger input value changes
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();
            if (triggerMode == HikTriggerMode.Software)
            {
                var triggerVal = GetInputValue(_triggerInput);
                if (triggerVal != _lastTriggerValue || _triggerInput.Connection == null)
                {
                    _camera.MV_CC_SetCommandValue_NET("TriggerSoftware");
                    _lastTriggerValue = triggerVal;
                }
            }

            // Get frame
            MyCamera.MV_FRAME_OUT stFrameOut = new MyCamera.MV_FRAME_OUT();
            int ret = _camera.MV_CC_GetImageBuffer_NET(ref stFrameOut, 1000);

            if (ret != MyCamera.MV_OK)
            {
                Error = $"Get frame failed: 0x{ret:X8}";
                return;
            }

            try
            {
                uint w = stFrameOut.stFrameInfo.nWidth;
                uint h = stFrameOut.stFrameInfo.nHeight;
                uint frameLen = stFrameOut.stFrameInfo.nFrameLen;
                IntPtr pBufAddr = stFrameOut.pBufAddr;

                if (w == 0 || h == 0 || pBufAddr == IntPtr.Zero)
                {
                    Error = "Invalid frame data";
                    return;
                }

                // Determine pixel format
                int pixelTypeInt = (int)stFrameOut.stFrameInfo.enPixelType;
                bool isMono = (pixelTypeInt & 0x01000000) != 0;

                Mat frame;
                if (isMono)
                {
                    int size = (int)(w * h);
                    byte[] data = new byte[size];
                    Marshal.Copy(pBufAddr, data, 0, size);
                    frame = new Mat((int)h, (int)w, MatType.CV_8UC1);
                    Marshal.Copy(data, 0, frame.Data, size);
                }
                else
                {
                    int expectedLen = (int)(w * h * 3);
                    if (frameLen >= (uint)expectedLen)
                    {
                        byte[] data = new byte[expectedLen];
                        Marshal.Copy(pBufAddr, data, 0, expectedLen);
                        frame = new Mat((int)h, (int)w, MatType.CV_8UC3);
                        Marshal.Copy(data, 0, frame.Data, expectedLen);
                        Cv2.CvtColor(frame, frame, ColorConversionCodes.RGB2BGR);
                    }
                    else
                    {
                        // Bayer pattern
                        int size = (int)(w * h);
                        byte[] data = new byte[size];
                        Marshal.Copy(pBufAddr, data, 0, size);
                        frame = new Mat((int)h, (int)w, MatType.CV_8UC1);
                        Marshal.Copy(data, 0, frame.Data, size);
                        Cv2.CvtColor(frame, frame, ColorConversionCodes.BayerRG2BGR);
                    }
                }

                SetOutputValue(_frameOutput, frame);
                SetPreview(frame);
                Error = null;
            }
            finally
            {
                _camera.MV_CC_FreeImageBuffer_NET(ref stFrameOut);
            }
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
            // Initialize SDK once
            if (!_sdkInitialized)
            {
                MyCamera.MV_CC_Initialize_NET();
                _sdkInitialized = true;
            }

            // Enumerate devices
            MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int ret = MyCamera.MV_CC_EnumDevices_NET(
                MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);

            if (ret != MyCamera.MV_OK)
            {
                Error = $"Enumerate devices failed: 0x{ret:X8}";
                return;
            }

            if (stDevList.nDeviceNum == 0)
            {
                Error = "No HIK cameras found";
                return;
            }

            if (deviceIndex >= (int)stDevList.nDeviceNum)
            {
                Error = $"Device index {deviceIndex} out of range (found {stDevList.nDeviceNum})";
                return;
            }

            // Get device info
            MyCamera.MV_CC_DEVICE_INFO stDevInfo =
                (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                    stDevList.pDeviceInfo[deviceIndex],
                    typeof(MyCamera.MV_CC_DEVICE_INFO))!;

            // Create camera instance
            _camera = new MyCamera();

            // Create device
            ret = _camera.MV_CC_CreateDevice_NET(ref stDevInfo);
            if (ret != MyCamera.MV_OK)
            {
                Error = $"Create device failed: 0x{ret:X8}";
                _camera = null;
                return;
            }

            // Open device
            ret = _camera.MV_CC_OpenDevice_NET();
            if (ret != MyCamera.MV_OK)
            {
                Error = $"Open device failed: 0x{ret:X8}";
                _camera.MV_CC_DestroyDevice_NET();
                _camera = null;
                return;
            }

            // Set optimal packet size for GigE cameras
            if (stDevInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
            {
                int packetSize = _camera.MV_CC_GetOptimalPacketSize_NET();
                if (packetSize > 0)
                {
                    _camera.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", packetSize);
                }
            }

            // Set trigger mode
            var triggerMode = _triggerMode.GetValue<HikTriggerMode>();
            if (triggerMode == HikTriggerMode.Continuous)
            {
                _camera.MV_CC_SetEnumValue_NET("TriggerMode", 0); // Off
            }
            else
            {
                _camera.MV_CC_SetEnumValue_NET("TriggerMode", 1); // On
                if (triggerMode == HikTriggerMode.Software)
                    _camera.MV_CC_SetEnumValue_NET("TriggerSource", 7); // Software
                else
                    _camera.MV_CC_SetEnumValue_NET("TriggerSource", 0); // Line0
            }

            // Set ROI if specified
            var width = _width.GetValue<int>();
            var height = _height.GetValue<int>();
            if (width > 0)
                _camera.MV_CC_SetIntValueEx_NET("Width", width);
            if (height > 0)
                _camera.MV_CC_SetIntValueEx_NET("Height", height);

            // Set initial exposure and gain
            var exposureTime = _exposureTime.GetValue<double>();
            _camera.MV_CC_SetFloatValue_NET("ExposureTime", (float)exposureTime);
            var gain = _gain.GetValue<double>();
            _camera.MV_CC_SetFloatValue_NET("Gain", (float)gain);

            // Start grabbing
            ret = _camera.MV_CC_StartGrabbing_NET();
            if (ret != MyCamera.MV_OK)
            {
                Error = $"Start grabbing failed: 0x{ret:X8}";
                _camera.MV_CC_CloseDevice_NET();
                _camera.MV_CC_DestroyDevice_NET();
                _camera = null;
                return;
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
                _camera.MV_CC_StopGrabbing_NET();
                _camera.MV_CC_CloseDevice_NET();
                _camera.MV_CC_DestroyDevice_NET();
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
