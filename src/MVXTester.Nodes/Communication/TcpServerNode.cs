using System.Net;
using System.Net.Sockets;
using System.Text;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Communication;

[NodeInfo("TCP Server", NodeCategories.Communication, Description = "TCP server with background receiving")]
public class TcpServerNode : BaseNode, IBackgroundNode
{
    private InputPort<string> _sendDataInput = null!;
    private OutputPort<string> _receivedDataOutput = null!;
    private OutputPort<bool> _isRunningOutput = null!;
    private NodeProperty _port = null!;

    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    private int _lastPort = -1;

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
        _isRunningOutput = AddOutput<bool>("IsRunning");
        _port = AddIntProperty("Port", "Port", 5000, 1, 65535, "TCP port to listen on");
    }

    public void StartBackground(CancellationToken ct)
    {
        _backgroundCt = ct;
        _backgroundRunning = true;
        _receiveThread = new Thread(BackgroundReceiveLoop)
        {
            IsBackground = true,
            Name = "TcpServer_BgReceive"
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
                // Accept pending connections
                if (_listener != null && _client == null && _listener.Pending())
                {
                    _client = _listener.AcceptTcpClient();
                    _client.ReceiveTimeout = 100;
                    _stream = _client.GetStream();
                }

                // Read available data
                if (_stream != null && _client != null && _client.Connected)
                {
                    if (_stream.DataAvailable)
                    {
                        var buffer = new byte[4096];
                        int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            lock (_bufferLock)
                            {
                                _receivedBuffer = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            }
                            IsDirty = true;
                        }
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (InvalidOperationException) { }

            Thread.Sleep(10);
        }
    }

    public override void Process()
    {
        try
        {
            var port = _port.GetValue<int>();

            // Start/restart listener if port changed
            if (_listener == null || port != _lastPort)
            {
                StopServer();
                StartServer(port);
                _lastPort = port;
            }

            // In non-background mode, poll directly (fallback)
            if (!_backgroundRunning)
            {
                if (_listener != null && _client == null && _listener.Pending())
                {
                    _client = _listener.AcceptTcpClient();
                    _client.ReceiveTimeout = 100;
                    _stream = _client.GetStream();
                }

                if (_stream != null && _client != null && _client.Connected)
                {
                    try
                    {
                        if (_stream.DataAvailable)
                        {
                            var buffer = new byte[4096];
                            int bytesRead = _stream.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                lock (_bufferLock)
                                {
                                    _receivedBuffer = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                                }
                            }
                        }
                    }
                    catch (IOException) { }
                    catch (SocketException) { }
                }
            }

            // Send data if connected
            var sendData = GetInputValue(_sendDataInput);
            if (!string.IsNullOrEmpty(sendData) && _stream != null && _client != null && _client.Connected)
            {
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(sendData);
                    _stream.Write(bytes, 0, bytes.Length);
                    _stream.Flush();
                }
                catch (IOException) { }
                catch (SocketException) { }
            }

            // Output buffered data
            lock (_bufferLock)
            {
                SetOutputValue(_receivedDataOutput, _receivedBuffer);
            }
            SetOutputValue(_isRunningOutput, _listener != null);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"TCP Server error: {ex.Message}";
        }
    }

    private void StartServer(int port)
    {
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
        }
        catch (Exception ex)
        {
            Error = $"Failed to start TCP server: {ex.Message}";
            _listener = null;
        }
    }

    private void StopServer()
    {
        try
        {
            _stream?.Close();
            _stream?.Dispose();
            _client?.Close();
            _client?.Dispose();
            _listener?.Stop();
        }
        catch { }
        finally
        {
            _stream = null;
            _client = null;
            _listener = null;
        }
    }

    public override void Cleanup()
    {
        StopBackground();
        StopServer();
        base.Cleanup();
    }
}
