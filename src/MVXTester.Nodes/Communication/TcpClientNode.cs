using System.Net.Sockets;
using System.Text;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Communication;

[NodeInfo("TCP Client", NodeCategories.Communication, Description = "TCP client with background receiving")]
public class TcpClientNode : BaseNode, IBackgroundNode
{
    private InputPort<string> _sendDataInput = null!;
    private OutputPort<string> _receivedDataOutput = null!;
    private OutputPort<bool> _isConnectedOutput = null!;
    private NodeProperty _host = null!;
    private NodeProperty _port = null!;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private string _lastHost = "";
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
        _isConnectedOutput = AddOutput<bool>("IsConnected");
        _host = AddStringProperty("Host", "Host", "127.0.0.1", "Server hostname or IP");
        _port = AddIntProperty("Port", "Port", 5000, 1, 65535, "Server port");
    }

    public void StartBackground(CancellationToken ct)
    {
        _backgroundCt = ct;
        _backgroundRunning = true;
        _receiveThread = new Thread(BackgroundReceiveLoop)
        {
            IsBackground = true,
            Name = "TcpClient_BgReceive"
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
            var host = _host.GetValue<string>();
            var port = _port.GetValue<int>();

            // Connect/reconnect if settings changed
            if (_client == null || !_client.Connected || host != _lastHost || port != _lastPort)
            {
                Disconnect();
                Connect(host, port);
                _lastHost = host;
                _lastPort = port;
            }

            // In non-background mode, poll directly (fallback)
            if (!_backgroundRunning && _stream != null && _client != null && _client.Connected)
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
            SetOutputValue(_isConnectedOutput, _client?.Connected ?? false);
            Error = null;
        }
        catch (Exception ex)
        {
            Error = $"TCP Client error: {ex.Message}";
        }
    }

    private void Connect(string host, int port)
    {
        try
        {
            _client = new TcpClient();
            _client.Connect(host, port);
            _client.ReceiveTimeout = 100;
            _stream = _client.GetStream();
        }
        catch (Exception ex)
        {
            Error = $"Failed to connect: {ex.Message}";
            _client?.Dispose();
            _client = null;
            _stream = null;
        }
    }

    private void Disconnect()
    {
        try
        {
            _stream?.Close();
            _stream?.Dispose();
            _client?.Close();
            _client?.Dispose();
        }
        catch { }
        finally
        {
            _stream = null;
            _client = null;
        }
    }

    public override void Cleanup()
    {
        StopBackground();
        Disconnect();
        base.Cleanup();
    }
}
