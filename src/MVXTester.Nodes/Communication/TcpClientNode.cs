using System.Net.Sockets;
using System.Text;
using MVXTester.Core.Models;
using MVXTester.Core.Registry;

namespace MVXTester.Nodes.Communication;

[NodeInfo("TCP Client", NodeCategories.Communication, Description = "TCP client for sending and receiving data")]
public class TcpClientNode : BaseNode
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
    private string _receivedData = "";
    private readonly object _lock = new();

    protected override void Setup()
    {
        _sendDataInput = AddInput<string>("Send Data");
        _receivedDataOutput = AddOutput<string>("Received Data");
        _isConnectedOutput = AddOutput<bool>("IsConnected");
        _host = AddStringProperty("Host", "Host", "127.0.0.1", "Server hostname or IP");
        _port = AddIntProperty("Port", "Port", 5000, 1, 65535, "Server port");
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

            // Read available data
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
                            lock (_lock)
                            {
                                _receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
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

            lock (_lock)
            {
                SetOutputValue(_receivedDataOutput, _receivedData);
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
        Disconnect();
        base.Cleanup();
    }
}
