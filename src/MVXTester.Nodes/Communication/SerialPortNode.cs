using System.IO.Ports;
using System.Text;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Communication;

public enum BaudRateOption
{
    Baud9600 = 9600,
    Baud19200 = 19200,
    Baud38400 = 38400,
    Baud57600 = 57600,
    Baud115200 = 115200
}

public enum DataModeOption
{
    ASCII,
    Hex
}

[NodeInfo("Serial Port", NodeCategories.Communication, Description = "Serial port communication with background receiving")]
public class SerialPortNode : BaseNode, IBackgroundNode
{
    private InputPort<string> _sendDataInput = null!;
    private OutputPort<string> _receivedDataOutput = null!;
    private OutputPort<bool> _isOpenOutput = null!;
    private NodeProperty _portName = null!;
    private NodeProperty _baudRate = null!;
    private NodeProperty _dataBits = null!;
    private NodeProperty _stopBits = null!;
    private NodeProperty _parity = null!;
    private NodeProperty _dataMode = null!;

    private SerialPort? _serialPort;
    private string _lastPortName = "";
    private int _lastBaudRate = -1;

    // Background receive buffer
    private string _receivedBuffer = "";
    private readonly object _bufferLock = new();
    private Thread? _receiveThread;
    private volatile bool _backgroundRunning;
    private CancellationToken _backgroundCt;

    protected override void Setup()
    {
        _sendDataInput = AddInput<string>("Send Data");
        _receivedDataOutput = AddOutput<string>("Received Data");
        _isOpenOutput = AddOutput<bool>("IsOpen");
        _portName = AddStringProperty("PortName", "Port Name", "COM1", "Serial port name (e.g., COM1)");
        _baudRate = AddEnumProperty("BaudRate", "Baud Rate", BaudRateOption.Baud9600, "Baud rate");
        _dataBits = AddIntProperty("DataBits", "Data Bits", 8, 5, 8, "Data bits");
        _stopBits = AddEnumProperty("StopBits", "Stop Bits", System.IO.Ports.StopBits.One, "Stop bits");
        _parity = AddEnumProperty("Parity", "Parity", System.IO.Ports.Parity.None, "Parity");
        _dataMode = AddEnumProperty("DataMode", "Data Mode", DataModeOption.ASCII, "Data mode (ASCII or Hex)");
    }

    public void StartBackground(CancellationToken ct)
    {
        _backgroundCt = ct;
        _backgroundRunning = true;
        _receiveThread = new Thread(BackgroundReceiveLoop)
        {
            IsBackground = true,
            Name = "SerialPort_BgReceive"
        };
        _receiveThread.Start();
    }

    public void StopBackground()
    {
        _backgroundRunning = false;
        _receiveThread?.Join(1000);
        _receiveThread = null;
    }

    private void BackgroundReceiveLoop()
    {
        while (_backgroundRunning && !_backgroundCt.IsCancellationRequested)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    int bytesAvailable = _serialPort.BytesToRead;
                    if (bytesAvailable > 0)
                    {
                        var dataMode = _dataMode.GetValue<DataModeOption>();
                        string data;
                        if (dataMode == DataModeOption.Hex)
                        {
                            var buffer = new byte[bytesAvailable];
                            _serialPort.Read(buffer, 0, bytesAvailable);
                            data = BitConverter.ToString(buffer).Replace("-", " ");
                        }
                        else
                        {
                            data = _serialPort.ReadExisting();
                        }

                        lock (_bufferLock)
                        {
                            _receivedBuffer = data;
                        }
                        IsDirty = true;
                    }
                }
            }
            catch (TimeoutException) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }

            Thread.Sleep(10);
        }
    }

    public override void Process()
    {
        try
        {
            var portName = _portName.GetValue<string>();
            var baudRate = (int)_baudRate.GetValue<BaudRateOption>();

            // Open/reopen port if settings changed
            if (_serialPort == null || !_serialPort.IsOpen || portName != _lastPortName || baudRate != _lastBaudRate)
            {
                ClosePort();
                OpenPort(portName, baudRate);
                _lastPortName = portName;
                _lastBaudRate = baudRate;
            }

            // In non-background mode, read directly (fallback for non-runtime execution)
            if (!_backgroundRunning && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    int bytesAvailable = _serialPort.BytesToRead;
                    if (bytesAvailable > 0)
                    {
                        var dataMode = _dataMode.GetValue<DataModeOption>();
                        if (dataMode == DataModeOption.Hex)
                        {
                            var buffer = new byte[bytesAvailable];
                            _serialPort.Read(buffer, 0, bytesAvailable);
                            lock (_bufferLock)
                            {
                                _receivedBuffer = BitConverter.ToString(buffer).Replace("-", " ");
                            }
                        }
                        else
                        {
                            lock (_bufferLock)
                            {
                                _receivedBuffer = _serialPort.ReadExisting();
                            }
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (IOException) { }
            }

            // Send data
            var sendData = GetInputValue(_sendDataInput);
            if (!string.IsNullOrEmpty(sendData) && _serialPort != null && _serialPort.IsOpen)
            {
                try
                {
                    var dataMode = _dataMode.GetValue<DataModeOption>();
                    if (dataMode == DataModeOption.Hex)
                    {
                        var hexParts = sendData.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                        var bytes = hexParts.Select(h => Convert.ToByte(h, 16)).ToArray();
                        _serialPort.Write(bytes, 0, bytes.Length);
                    }
                    else
                    {
                        _serialPort.Write(sendData);
                    }
                }
                catch (IOException) { }
                catch (FormatException ex)
                {
                    Error = $"Invalid hex format: {ex.Message}";
                    return;
                }
            }

            // Output buffered data
            lock (_bufferLock)
            {
                SetOutputValue(_receivedDataOutput, _receivedBuffer);
            }
            SetOutputValue(_isOpenOutput, _serialPort?.IsOpen ?? false);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"Serial Port error: {ex.Message}";
        }
    }

    private void OpenPort(string portName, int baudRate)
    {
        try
        {
            var dataBits = _dataBits.GetValue<int>();
            var stopBits = _stopBits.GetValue<StopBits>();
            var parity = _parity.GetValue<Parity>();

            _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
            {
                ReadTimeout = 100,
                WriteTimeout = 1000
            };
            _serialPort.Open();
        }
        catch (Exception ex)
        {
            Error = $"Failed to open serial port: {ex.Message}";
            _serialPort?.Dispose();
            _serialPort = null;
        }
    }

    private void ClosePort()
    {
        try
        {
            if (_serialPort != null && _serialPort.IsOpen)
                _serialPort.Close();
            _serialPort?.Dispose();
        }
        catch { }
        finally
        {
            _serialPort = null;
        }
    }

    public override void Cleanup()
    {
        StopBackground();
        ClosePort();
        base.Cleanup();
    }
}
