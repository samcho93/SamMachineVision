using OpenCvSharp;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MVXTester.Nodes.Input;

public enum CognexTriggerMode
{
    FreeRun,
    Software,
    Hardware
}

public enum CognexPixelFormat
{
    Mono8,
    Color24
}

/// <summary>
/// Cognex GigE Camera node – VisionPro 9.x (Cognex.Vision.* namespace).
/// Two assemblies are loaded at runtime via reflection:
///   • Cognex.Vision.Acquisition.Net.dll  (frame grabber, FIFO)
///   • Cognex.Vision.Core.Net.dll         (Image8Grey, pixel memory)
/// This avoids a compile-time reference to the native VisionPro SDK.
/// </summary>
// Hidden from palette - use unified CameraNode instead.
// Kept for backward compatibility (serialization) and as backend for CameraNode.
public class CognexGigECameraNode : BaseNode, IStreamingSource, IDeviceEnumerable
{
    // ── Ports & properties ──────────────────────────────────────────
    private InputPort<int> _triggerInput = null!;
    private OutputPort<Mat> _frameOutput = null!;
    private NodeProperty _deviceList = null!;
    private NodeProperty _triggerMode = null!;
    private NodeProperty _exposureTime = null!;
    private NodeProperty _brightness = null!;
    private NodeProperty _pixelFormat = null!;

    // ── SDK runtime state ───────────────────────────────────────────
    private static bool _sdkInitialized; // one-time Startup.Initialize()
    private string? _visionProBinDir;    // cached bin directory path
    private Assembly? _acqAssembly;      // Cognex.Vision.Acquisition.Net.dll
    private Assembly? _coreAssembly;     // Cognex.Vision.Core.Net.dll
    private object? _frameGrabbers;      // FrameGrabberGigEReadOnlyCollection
    private object? _selectedGrabber;    // IFrameGrabber
    private object? _acqFifo;            // IAcqFifo
    private bool _isOpen;
    private int _lastDeviceIndex = -1;
    private CognexPixelFormat _lastPixelFormat = CognexPixelFormat.Mono8;
    private int _lastTriggerValue;

    // ── Cached type references ──────────────────────────────────────
    // Acquisition assembly types
    private Type? _gigeCollectionType;          // FrameGrabberGigEReadOnlyCollection
    private Type? _iFrameGrabberType;           // IFrameGrabber
    private Type? _iAcqFifoType;                // IAcqFifo
    private Type? _iAcqExposureType;            // IAcqExposure
    private Type? _iAcqBrightnessType;          // IAcqBrightness
    private Type? _iAcqTriggerType;             // IAcqTrigger
    private Type? _acqTriggerModelType;         // AcqTriggerModelConstants enum
    private Type? _imagePixelFormatType;        // ImagePixelFormatConstants enum

    // Core assembly types
    private Type? _iImageType;                  // IImage
    private Type? _image8GreyType;              // Image8Grey
    private Type? _image24PlanarType;           // Image24PlanarColor
    private Type? _imageDataModeType;           // ImageDataModeConstants enum
    private Type? _iImage8PixelMemType;         // IImage8PixelMemory

    // ── Cached method / property references ─────────────────────────

    // FrameGrabberGigEReadOnlyCollection
    private PropertyInfo? _collCount;
    private MethodInfo? _collGetItem;

    // IFrameGrabber
    private PropertyInfo? _fgName;
    private PropertyInfo? _fgModel;
    private PropertyInfo? _fgSerialNumber;
    private MethodInfo? _fgCreateAcqFifo;          // (string, int, bool)
    private MethodInfo? _fgDisconnect;              // (bool)

    // IAcqFifo
    private MethodInfo? _fifoAcquire;               // (out int) → IImage
    private PropertyInfo? _fifoOutputPixelFormat;    // ImagePixelFormatConstants
    private PropertyInfo? _fifoOwnedExposure;
    private PropertyInfo? _fifoOwnedBrightness;
    private PropertyInfo? _fifoOwnedTrigger;
    private PropertyInfo? _fifoTimeout;
    private PropertyInfo? _fifoTimeoutEnabled;

    // IAcqExposure / IAcqBrightness / IAcqTrigger
    private PropertyInfo? _exposureValue;            // double Exposure
    private PropertyInfo? _brightnessValue;          // double Brightness
    private PropertyInfo? _triggerModelProp;          // AcqTriggerModelConstants TriggerModel
    private PropertyInfo? _triggerEnabledProp;        // bool TriggerEnabled

    // IImage
    private PropertyInfo? _imgWidth;
    private PropertyInfo? _imgHeight;

    // Image8Grey
    private MethodInfo? _get8GreyPixelMem;           // (mode, x, y, w, h) → IImage8PixelMemory

    // Image24PlanarColor
    private MethodInfo? _get24PlanarPixelMem;        // (mode, x, y, w, h, out pm0, out pm1, out pm2)

    // IImage8PixelMemory
    private PropertyInfo? _pmScan0;
    private PropertyInfo? _pmStride;

    // ── Cached enum values ──────────────────────────────────────────
    private object? _readMode;                       // ImageDataModeConstants.Read  (=1)
    private object? _trigFreeRun;                     // AcqTriggerModelConstants.FreeRun (=4)
    private object? _trigManual;                      // AcqTriggerModelConstants.Manual  (=0)
    private object? _trigAuto;                        // AcqTriggerModelConstants.Auto    (=1)
    private object? _pxGrey8;                         // ImagePixelFormatConstants.Grey8  (=1)
    private object? _pxPlanarRGB8;                    // ImagePixelFormatConstants.PlanarRGB8 (=12)

    // ═════════════════════════════════════════════════════════════════
    protected override void Setup()
    {
        _triggerInput = AddInput<int>("Trigger");
        _frameOutput = AddOutput<Mat>("Frame");
        _deviceList = AddDeviceListProperty("DeviceList", "Camera", -1, "Select Cognex GigE camera");
        _triggerMode = AddEnumProperty("TriggerMode", "Trigger Mode", CognexTriggerMode.FreeRun, "Trigger mode");
        _exposureTime = AddDoubleProperty("ExposureTime", "Exposure Time (us)", 10000.0, 10.0, 10000000.0, "Exposure time in microseconds");
        _brightness = AddDoubleProperty("Brightness", "Brightness", 0.5, 0.0, 1.0, "Brightness (0.0 ~ 1.0)");
        _pixelFormat = AddEnumProperty("PixelFormat", "Pixel Format", CognexPixelFormat.Mono8, "Camera pixel format");

        EnumerateDevices();
    }

    // ═══════════════════════════════════════════════════════════════
    //  Device Enumeration
    // ═══════════════════════════════════════════════════════════════

    public void EnumerateDevices()
    {
        var devices = new List<(string Name, int Index)>();
        try
        {
            // Use LoadAssemblies (NOT full InitializeSdk) to avoid starting
            // VisionPro's background GigE services which block other SDKs
            // (e.g. HIK MVS) from accessing the same camera.
            if (LoadAssemblies() && _gigeCollectionType != null)
            {
                // FrameGrabberGigEReadOnlyCollection ctor sends GigE discovery
                // broadcasts and works without Startup.Initialize().
                object? fgs = null;
                try
                {
                    fgs = Activator.CreateInstance(_gigeCollectionType);
                    if (fgs != null)
                    {
                        int count = Convert.ToInt32(_collCount?.GetValue(fgs) ?? 0);
                        for (int i = 0; i < count; i++)
                        {
                            // GetItem() requires Startup.Initialize() for type factories.
                            // Without it, use generic names. Full details available after OpenCamera.
                            string displayName;
                            try
                            {
                                var fg = _collGetItem?.Invoke(fgs, new object[] { i });
                                if (fg == null) { displayName = $"[GigE] Cognex Camera {i}"; }
                                else
                                {
                                    string name = _fgName?.GetValue(fg)?.ToString() ?? "";
                                    string model = _fgModel?.GetValue(fg)?.ToString() ?? "";
                                    string serial = _fgSerialNumber?.GetValue(fg)?.ToString() ?? "";
                                    string label = !string.IsNullOrEmpty(model) ? model : name;
                                    if (string.IsNullOrEmpty(label)) label = $"Cognex Camera {i}";
                                    displayName = string.IsNullOrEmpty(serial)
                                        ? $"[GigE] {label}" : $"[GigE] {label} ({serial})";
                                }
                            }
                            catch
                            {
                                displayName = $"[GigE] Cognex Camera {i}";
                            }
                            devices.Add((displayName, i));
                        }
                    }
                }
                finally
                {
                    // Dispose immediately — don't hold the GigE interface
                    try { if (fgs is IDisposable d) d.Dispose(); } catch { }
                }
            }
        }
        catch { }

        // Always update device options, even if empty
        _deviceList.UpdateDeviceOptions(devices);

        if (devices.Count > 0 && _deviceList.GetValue<int>() < 0)
            _deviceList.SetValue(devices[0].Index);
    }

    // ═══════════════════════════════════════════════════════════════
    //  Process  (grab one frame)
    // ═══════════════════════════════════════════════════════════════

    public override void Process()
    {
        try
        {
            var deviceIndex = _deviceList.GetValue<int>();
            if (deviceIndex < 0) { Error = "No camera selected"; return; }

            var pixelFormat = _pixelFormat.GetValue<CognexPixelFormat>();

            if (!_isOpen || deviceIndex != _lastDeviceIndex || pixelFormat != _lastPixelFormat)
            {
                CloseCamera();
                OpenCamera(deviceIndex);
                _lastDeviceIndex = deviceIndex;
                _lastPixelFormat = pixelFormat;
            }

            if (!_isOpen || _acqFifo == null) return;

            // ─── Apply exposure ───
            try
            {
                var ep = _fifoOwnedExposure?.GetValue(_acqFifo);
                if (ep != null) _exposureValue?.SetValue(ep, _exposureTime.GetValue<double>());
            }
            catch { }

            // ─── Apply brightness ───
            try
            {
                var bp = _fifoOwnedBrightness?.GetValue(_acqFifo);
                if (bp != null) _brightnessValue?.SetValue(bp, _brightness.GetValue<double>());
            }
            catch { }

            // ─── Software trigger gate ───
            var tMode = _triggerMode.GetValue<CognexTriggerMode>();
            if (tMode == CognexTriggerMode.Software)
            {
                var tv = GetInputValue(_triggerInput);
                if (tv == _lastTriggerValue && _triggerInput.Connection != null) return;
                _lastTriggerValue = tv;
            }

            // ─── Acquire ───
            if (_fifoAcquire == null) { Error = "Acquire method not found"; return; }

            var acqArgs = new object?[] { 0 }; // out int triggerNumber
            object? img = _fifoAcquire.Invoke(_acqFifo, acqArgs);
            if (img == null) { Error = "Acquire returned null"; return; }

            try
            {
                int w = Convert.ToInt32(_imgWidth?.GetValue(img) ?? 0);
                int h = Convert.ToInt32(_imgHeight?.GetValue(img) ?? 0);
                if (w <= 0 || h <= 0) { Error = "Invalid image size"; return; }

                Mat frame;

                if (_image8GreyType != null && _image8GreyType.IsInstanceOfType(img))
                    frame = ConvertGrey8ToMat(img, w, h);
                else if (_image24PlanarType != null && _image24PlanarType.IsInstanceOfType(img))
                    frame = ConvertPlanar24ToMat(img, w, h);
                else
                    frame = ConvertGrey8ToMat(img, w, h); // fallback

                SetOutputValue(_frameOutput, frame);
                SetPreview(frame);
                Error = null;
            }
            finally
            {
                try { if (img is IDisposable d) d.Dispose(); } catch { }
            }
        }
        catch (Exception ex)
        {
            Error = $"Cognex Camera: {ex.InnerException?.Message ?? ex.Message}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Image → Mat conversion
    // ═══════════════════════════════════════════════════════════════

    private Mat ConvertGrey8ToMat(object img, int w, int h)
    {
        // Image8Grey.Get8GreyPixelMemory(Read, 0, 0, w, h) → IImage8PixelMemory
        var pm = _get8GreyPixelMem?.Invoke(img,
            new object[] { _readMode!, 0, 0, w, h })
            ?? throw new Exception("Get8GreyPixelMemory failed");

        try
        {
            IntPtr scan0 = (IntPtr)(_pmScan0?.GetValue(pm) ?? IntPtr.Zero);
            int stride = Convert.ToInt32(_pmStride?.GetValue(pm) ?? 0);
            if (scan0 == IntPtr.Zero || stride <= 0)
                throw new Exception("Invalid pixel memory");

            var mat = new Mat(h, w, MatType.CV_8UC1);

            if (stride == w)
            {
                int size = w * h;
                byte[] buf = new byte[size];
                Marshal.Copy(scan0, buf, 0, size);
                Marshal.Copy(buf, 0, mat.Data, size);
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    byte[] row = new byte[w];
                    Marshal.Copy(IntPtr.Add(scan0, y * stride), row, 0, w);
                    Marshal.Copy(row, 0, IntPtr.Add(mat.Data, y * w), w);
                }
            }
            return mat;
        }
        finally { DisposePM(pm); }
    }

    private Mat ConvertPlanar24ToMat(object img, int w, int h)
    {
        // Image24PlanarColor.Get24PlanarColorPixelMemory(Read, 0,0,w,h, out pm0, out pm1, out pm2)
        if (_get24PlanarPixelMem == null)
            return ConvertGrey8ToMat(img, w, h); // fallback

        var args = new object?[] { _readMode!, 0, 0, w, h, null, null, null };
        _get24PlanarPixelMem.Invoke(img, args);

        object? pm0 = args[5]; // plane 0 (R or Y)
        object? pm1 = args[6]; // plane 1 (G or Cb)
        object? pm2 = args[7]; // plane 2 (B or Cr)

        try
        {
            var mat = new Mat(h, w, MatType.CV_8UC3);
            byte[] bgr = new byte[w * h * 3];

            // VisionPro default colour space is RGB:
            // plane0=R → BGR channel 2, plane1=G → 1, plane2=B → 0
            var planes = new[] { pm0, pm1, pm2 };
            int[] bgrMap = { 2, 1, 0 }; // R→2, G→1, B→0

            for (int p = 0; p < 3; p++)
            {
                if (planes[p] == null) continue;
                IntPtr scan0 = (IntPtr)(_pmScan0?.GetValue(planes[p]!) ?? IntPtr.Zero);
                int stride = Convert.ToInt32(_pmStride?.GetValue(planes[p]!) ?? 0);
                if (scan0 == IntPtr.Zero || stride <= 0) continue;

                int ch = bgrMap[p];
                for (int y = 0; y < h; y++)
                {
                    byte[] row = new byte[w];
                    Marshal.Copy(IntPtr.Add(scan0, y * stride), row, 0, w);
                    int off = y * w * 3;
                    for (int x = 0; x < w; x++)
                        bgr[off + x * 3 + ch] = row[x];
                }
            }

            Marshal.Copy(bgr, 0, mat.Data, bgr.Length);
            return mat;
        }
        finally
        {
            DisposePM(pm0); DisposePM(pm1); DisposePM(pm2);
        }
    }

    private void DisposePM(object? pm)
    {
        if (pm == null) return;
        try { if (pm is IDisposable d) d.Dispose(); } catch { }
    }

    // ═══════════════════════════════════════════════════════════════
    //  SDK loading  (VisionPro 9.x – two assemblies)
    // ═══════════════════════════════════════════════════════════════

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    /// <summary>
    /// Phase 1 — Load DLLs and cache types/members only.
    /// Does NOT call Startup.Initialize() so VisionPro's background GigE
    /// services are not started. Safe to call during EnumerateDevices().
    /// </summary>
    private bool LoadAssemblies()
    {
        if (_acqAssembly != null && _coreAssembly != null) return true;

        try
        {
            string? binDir = FindVisionProBin();
            if (binDir == null)
            {
                Error = "VisionPro SDK not found. Install Cognex VisionPro.";
                return false;
            }

            // Add VisionPro bin to native DLL search path
            SetDllDirectory(binDir);
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (!path.Contains(binDir, StringComparison.OrdinalIgnoreCase))
                Environment.SetEnvironmentVariable("PATH", binDir + ";" + path);

            _visionProBinDir = binDir;

            var acqDll = Path.Combine(binDir, "Cognex.Vision.Acquisition.Net.dll");
            var coreDll = Path.Combine(binDir, "Cognex.Vision.Core.Net.dll");

            if (!File.Exists(acqDll)) { Error = $"Not found: {acqDll}"; return false; }
            if (!File.Exists(coreDll)) { Error = $"Not found: {coreDll}"; return false; }

            _coreAssembly = Assembly.LoadFrom(coreDll);
            _acqAssembly = Assembly.LoadFrom(acqDll);

            CacheTypes();
            CacheMembersFromAcq();
            CacheMembersFromCore();
            CacheEnumValues();

            if (_gigeCollectionType == null) { Error = "FrameGrabberGigEReadOnlyCollection not found"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            Error = $"VisionPro SDK load failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Phase 2 — Full SDK initialization.
    /// Calls Startup.Initialize() which starts VisionPro's background GigE
    /// network services. Only call this when actually opening a camera.
    /// WARNING: Once called, VisionPro holds the GigE interface for the
    /// lifetime of the process, blocking other GigE SDKs (e.g. HIK MVS).
    /// </summary>
    private bool InitializeSdk()
    {
        if (_sdkInitialized) return true;
        if (!LoadAssemblies()) return false;

        try
        {
            var startupDll = Path.Combine(_visionProBinDir!, "Cognex.Vision.Startup.Net.dll");
            if (File.Exists(startupDll))
            {
                var startupAsm = Assembly.LoadFrom(startupDll);
                var startupType = startupAsm.GetType("Cognex.Vision.Startup");
                var initMethod = startupType?.GetMethod("Initialize",
                    BindingFlags.Static | BindingFlags.Public);
                if (initMethod != null)
                {
                    var parms = initMethod.GetParameters();
                    var defaults = parms.Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                    initMethod.Invoke(null, defaults);
                }
            }
            _sdkInitialized = true;

            // Register types after startup
            CallRegistrationInit(_acqAssembly!, "Cognex.Vision.Acquisition.Registration_");
            CallRegistrationInit(_coreAssembly!, "Cognex.Vision.Registration_");

            return true;
        }
        catch (Exception ex)
        {
            Error = $"VisionPro initialization failed: {ex.Message}";
            return false;
        }
    }

    private static void CallRegistrationInit(Assembly asm, string typeName)
    {
        try
        {
            var regType = asm.GetType(typeName);
            var init = regType?.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public);
            init?.Invoke(null, null);
        }
        catch { }
    }

    private string? FindVisionProBin()
    {
        // 1. App directory
        var appBin = AppDomain.CurrentDomain.BaseDirectory;
        if (File.Exists(Path.Combine(appBin, "Cognex.Vision.Acquisition.Net.dll")))
            return appBin;

        // 2. Known paths (x64 first)
        var known = new[]
        {
            @"C:\Program Files\Cognex\VisionPro\bin",
            @"C:\Program Files (x86)\Cognex\VisionPro\bin",
        };
        foreach (var p in known)
            if (File.Exists(Path.Combine(p, "Cognex.Vision.Acquisition.Net.dll")))
                return p;

        // 3. Search versioned VisionPro folders
        foreach (var pfDir in new[] { @"C:\Program Files\Cognex", @"C:\Program Files (x86)\Cognex" })
        {
            if (!Directory.Exists(pfDir)) continue;
            foreach (var subDir in Directory.GetDirectories(pfDir, "VisionPro*")
                         .OrderByDescending(d => d))
            {
                var candidate = Path.Combine(subDir, "bin");
                if (File.Exists(Path.Combine(candidate, "Cognex.Vision.Acquisition.Net.dll")))
                    return candidate;
            }
        }

        return null;
    }

    private void CacheTypes()
    {
        // Acquisition types
        _gigeCollectionType = _acqAssembly!.GetType("Cognex.Vision.Acquisition.GigE.FrameGrabberGigEReadOnlyCollection");
        _iFrameGrabberType = _acqAssembly.GetType("Cognex.Vision.Acquisition.IFrameGrabber");
        _iAcqFifoType = _acqAssembly.GetType("Cognex.Vision.Acquisition.IAcqFifo");
        _iAcqExposureType = _acqAssembly.GetType("Cognex.Vision.Acquisition.IAcqExposure");
        _iAcqBrightnessType = _acqAssembly.GetType("Cognex.Vision.Acquisition.IAcqBrightness");
        _iAcqTriggerType = _acqAssembly.GetType("Cognex.Vision.Acquisition.IAcqTrigger");
        _acqTriggerModelType = _acqAssembly.GetType("Cognex.Vision.Acquisition.AcqTriggerModelConstants");
        _imagePixelFormatType = _acqAssembly.GetType("Cognex.Vision.Acquisition.ImagePixelFormatConstants");

        // Core types
        _iImageType = _coreAssembly!.GetType("Cognex.Vision.IImage");
        _image8GreyType = _coreAssembly.GetType("Cognex.Vision.Image8Grey");
        _image24PlanarType = _coreAssembly.GetType("Cognex.Vision.Image24PlanarColor");
        _imageDataModeType = _coreAssembly.GetType("Cognex.Vision.ImageDataModeConstants");
        _iImage8PixelMemType = _coreAssembly.GetType("Cognex.Vision.IImage8PixelMemory");
    }

    private void CacheMembersFromAcq()
    {
        // FrameGrabberGigEReadOnlyCollection
        if (_gigeCollectionType != null)
        {
            _collCount = _gigeCollectionType.GetProperty("Count");
            _collGetItem = _gigeCollectionType.GetMethod("GetItem", new[] { typeof(int) });
        }

        // IFrameGrabber
        if (_iFrameGrabberType != null)
        {
            _fgName = _iFrameGrabberType.GetProperty("Name");
            _fgModel = _iFrameGrabberType.GetProperty("Model");
            _fgSerialNumber = _iFrameGrabberType.GetProperty("SerialNumber");
            // CreateAcqFifo(string videoFormat, int cameraPort, bool autoPrepare)
            _fgCreateAcqFifo = _iFrameGrabberType.GetMethod("CreateAcqFifo",
                new[] { typeof(string), typeof(int), typeof(bool) });
            // Disconnect(bool allowRecovery)
            _fgDisconnect = _iFrameGrabberType.GetMethod("Disconnect", new[] { typeof(bool) });
        }

        // IAcqFifo
        if (_iAcqFifoType != null)
        {
            _fifoAcquire = _iAcqFifoType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "Acquire" && m.GetParameters().Length == 1);
            _fifoOutputPixelFormat = _iAcqFifoType.GetProperty("OutputPixelFormat");
            _fifoOwnedExposure = _iAcqFifoType.GetProperty("OwnedExposureParams");
            _fifoOwnedBrightness = _iAcqFifoType.GetProperty("OwnedBrightnessParams");
            _fifoOwnedTrigger = _iAcqFifoType.GetProperty("OwnedTriggerParams");
            _fifoTimeout = _iAcqFifoType.GetProperty("Timeout");
            _fifoTimeoutEnabled = _iAcqFifoType.GetProperty("TimeoutEnabled");
        }

        // IAcqExposure
        if (_iAcqExposureType != null)
            _exposureValue = _iAcqExposureType.GetProperty("Exposure");

        // IAcqBrightness
        if (_iAcqBrightnessType != null)
            _brightnessValue = _iAcqBrightnessType.GetProperty("Brightness");

        // IAcqTrigger
        if (_iAcqTriggerType != null)
        {
            _triggerModelProp = _iAcqTriggerType.GetProperty("TriggerModel");
            _triggerEnabledProp = _iAcqTriggerType.GetProperty("TriggerEnabled");
        }
    }

    private void CacheMembersFromCore()
    {
        // IImage
        if (_iImageType != null)
        {
            _imgWidth = _iImageType.GetProperty("Width");
            _imgHeight = _iImageType.GetProperty("Height");
        }

        // Image8Grey.Get8GreyPixelMemory(mode, x, y, w, h)
        if (_image8GreyType != null && _imageDataModeType != null)
        {
            _get8GreyPixelMem = _image8GreyType.GetMethod("Get8GreyPixelMemory",
                new[] { _imageDataModeType, typeof(int), typeof(int), typeof(int), typeof(int) });
        }

        // Image24PlanarColor.Get24PlanarColorPixelMemory(mode, x, y, w, h, out pm0, out pm1, out pm2)
        if (_image24PlanarType != null && _imageDataModeType != null && _iImage8PixelMemType != null)
        {
            var pmByRef = _iImage8PixelMemType.MakeByRefType();
            _get24PlanarPixelMem = _image24PlanarType.GetMethod("Get24PlanarColorPixelMemory",
                new[] { _imageDataModeType, typeof(int), typeof(int), typeof(int), typeof(int),
                        pmByRef, pmByRef, pmByRef });
        }

        // IImage8PixelMemory
        if (_iImage8PixelMemType != null)
        {
            _pmScan0 = _iImage8PixelMemType.GetProperty("Scan0");
            _pmStride = _iImage8PixelMemType.GetProperty("Stride");
        }
    }

    private void CacheEnumValues()
    {
        if (_imageDataModeType != null)
            try { _readMode = Enum.Parse(_imageDataModeType, "Read"); } catch { }

        if (_acqTriggerModelType != null)
        {
            try { _trigFreeRun = Enum.Parse(_acqTriggerModelType, "FreeRun"); } catch { }
            try { _trigManual = Enum.Parse(_acqTriggerModelType, "Manual"); } catch { }
            try { _trigAuto = Enum.Parse(_acqTriggerModelType, "Auto"); } catch { }
        }

        if (_imagePixelFormatType != null)
        {
            try { _pxGrey8 = Enum.Parse(_imagePixelFormatType, "Grey8"); } catch { }
            try { _pxPlanarRGB8 = Enum.Parse(_imagePixelFormatType, "PlanarRGB8"); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  Camera open / close
    // ═══════════════════════════════════════════════════════════════

    private void OpenCamera(int deviceIndex)
    {
        try
        {
            // Full SDK init (Startup.Initialize) is required for CreateAcqFifo.
            // This starts VisionPro's GigE background services.
            if (!InitializeSdk()) return;

            if (_frameGrabbers == null)
            {
                _frameGrabbers = Activator.CreateInstance(_gigeCollectionType!);
                if (_frameGrabbers == null) { Error = "Failed to create GigE enumerator"; return; }
            }

            int count = Convert.ToInt32(_collCount?.GetValue(_frameGrabbers) ?? 0);
            if (count == 0) { Error = "No Cognex GigE cameras found"; return; }
            if (deviceIndex >= count) { Error = $"Index {deviceIndex} out of range ({count})"; return; }

            // Get frame grabber
            _selectedGrabber = _collGetItem?.Invoke(_frameGrabbers, new object[] { deviceIndex });
            if (_selectedGrabber == null) { Error = "Failed to get frame grabber"; return; }

            // Determine video format from available formats
            var pixFmt = _pixelFormat.GetValue<CognexPixelFormat>();
            string videoFormat = PickVideoFormat(pixFmt);

            // CreateAcqFifo(videoFormat, cameraPort=0, autoPrepare=true)
            if (_fgCreateAcqFifo == null) { Error = "CreateAcqFifo not found"; return; }
            _acqFifo = _fgCreateAcqFifo.Invoke(_selectedGrabber, new object[] { videoFormat, 0, true });
            if (_acqFifo == null) { Error = "CreateAcqFifo returned null"; return; }

            // Set output pixel format on FIFO
            if (_fifoOutputPixelFormat != null && _pxGrey8 != null)
            {
                var outFmt = pixFmt == CognexPixelFormat.Color24 ? (_pxPlanarRGB8 ?? _pxGrey8) : _pxGrey8;
                try { _fifoOutputPixelFormat.SetValue(_acqFifo, outFmt); } catch { }
            }

            // Trigger mode
            try
            {
                var tp = _fifoOwnedTrigger?.GetValue(_acqFifo);
                if (tp != null && _triggerModelProp != null)
                {
                    var tm = _triggerMode.GetValue<CognexTriggerMode>() switch
                    {
                        CognexTriggerMode.FreeRun => _trigFreeRun,
                        CognexTriggerMode.Software => _trigManual,
                        CognexTriggerMode.Hardware => _trigAuto,
                        _ => _trigFreeRun
                    };
                    if (tm != null) _triggerModelProp.SetValue(tp, tm);
                }
            }
            catch { }

            // Timeout
            try
            {
                _fifoTimeoutEnabled?.SetValue(_acqFifo, true);
                _fifoTimeout?.SetValue(_acqFifo, 5000.0);
            }
            catch { }

            // Initial exposure
            try
            {
                var ep = _fifoOwnedExposure?.GetValue(_acqFifo);
                if (ep != null) _exposureValue?.SetValue(ep, _exposureTime.GetValue<double>());
            }
            catch { }

            // Initial brightness
            try
            {
                var bp = _fifoOwnedBrightness?.GetValue(_acqFifo);
                if (bp != null) _brightnessValue?.SetValue(bp, _brightness.GetValue<double>());
            }
            catch { }

            _isOpen = true;
            Error = null;
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException?.Message ?? ex.Message;
            if (msg.Contains("Security violation", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("license", StringComparison.OrdinalIgnoreCase))
            {
                Error = "VisionPro license required. Use 'HIK Camera' node instead (supports GigE Vision standard cameras).";
            }
            else
            {
                Error = $"Open camera failed: {msg}";
            }
            _isOpen = false;
        }
    }

    /// <summary>
    /// Pick a video format string. VisionPro requires a format string that matches
    /// one of the camera's available video formats. Common GigE format strings:
    ///   "Generic GigEVision (Mono)"  /  "Generic GigEVision (Color)"
    /// If the camera doesn't support these generic names, pick the first available.
    /// </summary>
    private string PickVideoFormat(CognexPixelFormat desired)
    {
        try
        {
            if (_selectedGrabber == null) return "Generic GigEVision (Mono)";

            var avfProp = _selectedGrabber.GetType().GetProperty("AvailableVideoFormats");
            if (avfProp?.GetValue(_selectedGrabber) is System.Collections.Specialized.StringCollection formats && formats.Count > 0)
            {
                string keyword = desired == CognexPixelFormat.Color24 ? "Color" : "Mono";

                // Try exact match first
                foreach (string? f in formats)
                    if (f != null && f.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                        return f;

                // Fallback: first available format
                return formats[0]!;
            }
        }
        catch { }

        return desired == CognexPixelFormat.Color24
            ? "Generic GigEVision (Color)"
            : "Generic GigEVision (Mono)";
    }

    private void CloseCamera()
    {
        try
        {
            if (_acqFifo != null)
            {
                try { if (_acqFifo is IDisposable d) d.Dispose(); } catch { }
                _acqFifo = null;
            }

            if (_selectedGrabber != null)
            {
                try { _fgDisconnect?.Invoke(_selectedGrabber, new object[] { true }); } catch { }
                _selectedGrabber = null;
            }
        }
        catch { }
        finally { _isOpen = false; }
    }

    private void DisposeFrameGrabbers()
    {
        try { if (_frameGrabbers is IDisposable d) d.Dispose(); } catch { }
        _frameGrabbers = null;
    }

    public override void Cleanup()
    {
        CloseCamera();
        DisposeFrameGrabbers();
        base.Cleanup();
    }
}
