using System.Buffers;
namespace ShuttleManager.Shared.Services.TcpOfClient;
public interface ITcpClientService
{
    public Task<bool> ConnectAsync(string host, int port);
    public Task SendAsync(ReadOnlyMemory<byte> data);
    public Task<ReadOnlySequence<byte>> ReceiveAsync(int length);
    Task<string?> ReceiveStringAsync(CancellationToken cancellationToken = default);
    void Disconnect();
}