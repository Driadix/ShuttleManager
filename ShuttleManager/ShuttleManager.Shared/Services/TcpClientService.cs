using ShuttleManager.Shared.Interfaces;
using System.Buffers;
using System.Net.Sockets;
namespace ShuttleManager.Shared.Services;

public class TcpClientService : ITcpClientService
{
    private TcpClient? _tcpClient;
    private Stream? _networkStream;

    public async Task<bool> ConnectAsync(string host, int port)
    {
        try
        {
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port);

            _networkStream = _tcpClient.GetStream();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data)
    {
        if (_networkStream == null) throw new InvalidOperationException("Not connected");
        await _networkStream.WriteAsync(data);
        await _networkStream.FlushAsync();
    }

    public async Task<ReadOnlySequence<byte>> ReceiveAsync(int length)
    {
        if (_networkStream == null) throw new InvalidOperationException("Not connected");

        var buffer = new byte[length];
        int totalRead = 0;

        while (totalRead < length)
        {
            int read = await _networkStream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead));
            if (read == 0)
                throw new IOException("Connection closed by remote host.");
            totalRead += read;
        }

        return new ReadOnlySequence<byte>(buffer);
    }

    public async Task<string?> ReceiveStringAsync(CancellationToken cancellationToken)
    {
        if (_networkStream == null) throw new InvalidOperationException("Not connected");

        using var reader = new StreamReader(_networkStream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

        try
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            return line;
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
    }

    public void Disconnect()
    {
        _networkStream?.Close();
        _networkStream = null;
        _tcpClient?.Close();
        _tcpClient = null;
    }
}