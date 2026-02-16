
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;

namespace ShuttleManager.Shared.Services.OtaUpdate;

public sealed class OtaUpdateService : IOtaUpdateService
{

    private readonly ILogger<OtaUpdateService> _logger;

    public OtaUpdateService(ILogger<OtaUpdateService> logger) => _logger = logger;

    private const byte CMD_INIT = 0x01;
    private const byte CMD_ERASE = 0x02;
    private const byte CMD_WRITE = 0x03;
    private const byte CMD_RUN = 0x04;

    private const byte RESP_OK = 0xAA;
    private const byte RESP_FAIL = 0xFF;

    private const int STM_PORT = 8080;
    private const int ESP_PORT = 8081;

    private const uint STM_BASE_ADDRESS = 0x08000000;

    public async Task<OtaResult> RunAsync(
        string ip,
        string filePath,
        OtaTarget target,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {
        if (!File.Exists(filePath))
            return OtaResult.Fail($"File not found {filePath}");

        if (Path.GetExtension(filePath).ToLower() != ".bin")
            return OtaResult.Fail("Only .bin supported");

        var firmware = await File.ReadAllBytesAsync(filePath, token);

        try
        {
            return target == OtaTarget.Stm32
                ? await RunStmAsync(ip, firmware, progress, token)
                : await RunEspAsync(ip, firmware, progress, token);
        }
        catch (OperationCanceledException)
        {
            return OtaResult.Fail("OTA Cancelled");
        }
        catch (Exception ex)
        {
            return OtaResult.Fail(ex.Message);
        }
    }

    // ================= STM =================

    private async Task<OtaResult> RunStmAsync(
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {

        _logger.LogInformation("Starting STM32 OTA update to {Ip}", ip);

        using var client = new TcpClient();
        await client.ConnectAsync(ip, STM_PORT);
        using var stream = client.GetStream();

        // INIT
        _logger.LogDebug("Sending CMD_INIT");
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);

        // ERASE
        await SendByte(stream, CMD_ERASE, token);
        await EnsureOk(stream, token);

        int totalBlocks = (int)Math.Ceiling(fw.Length / 256.0);
        int offset = 0;

        for (int i = 0; i < totalBlocks; i++)
        {
            token.ThrowIfCancellationRequested();

            byte[] block = new byte[256];
            int len = Math.Min(256, fw.Length - offset);
            Buffer.BlockCopy(fw, offset, block, 0, len);

            uint addr = STM_BASE_ADDRESS + (uint)offset;
            _logger.LogDebug("Sending CMD_WRITE for block {Block}/{Total}, address 0x{Addr:X8}, size {Size}", i + 1, totalBlocks, addr, len);
            await SendByte(stream, CMD_WRITE, token);

            var addrBytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(addrBytes, addr);
            await stream.WriteAsync(addrBytes, token);

            await stream.WriteAsync(block, token);

            await EnsureOk(stream, token);

            offset += len;
            
            progress?.Report(new OtaProgress(offset, fw.Length));
        }
        _logger.LogDebug("Sending CMD_RUN");
        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        _logger.LogInformation("STM32 OTA update completed successfully");
        return OtaResult.Success();
    }

    // ================= ESP =================

    private async Task<OtaResult> RunEspAsync(
        string ip,
        byte[] fw,
        IProgress<OtaProgress>? progress,
        CancellationToken token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(ip, ESP_PORT);
        using var stream = client.GetStream();

        // INIT
        await SendByte(stream, CMD_INIT, token);

        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fw.Length);
        await stream.WriteAsync(sizeBytes, token);

        await EnsureOk(stream, token);

        int offset = 0;
        const int chunkSize = 2048;

        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();

            int len = Math.Min(chunkSize, fw.Length - offset);

            await SendByte(stream, CMD_WRITE, token);

            var lenBytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(lenBytes, (ushort)len);
            await stream.WriteAsync(lenBytes, token);

            await stream.WriteAsync(fw, offset, len, token);

            await EnsureOk(stream, token);

            offset += len;

            progress?.Report(new OtaProgress(offset, fw.Length));
        }

        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

        return OtaResult.Success();
    }

    // ================= Helpers =================

    private static async Task SendByte(NetworkStream stream, byte value, CancellationToken token)
    {
        var buffer = new byte[] { value };
        await stream.WriteAsync(buffer, token);
    }

    private static async Task EnsureOk(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[1];
        int read = await stream.ReadAsync(buffer, token);

        if (read != 1 || buffer[0] != RESP_OK)
        {
            Console.WriteLine("Received unexpected response: 0x{Resp:X2}", buffer);
            throw new InvalidOperationException($"Device returned FAIL: {buffer}");
        }
            
    }
}


public enum OtaTarget
{
    Stm32,
    Esp32
}

public sealed record OtaProgress(long Sent, long Total)
{
    public int Percent => (int)((Sent * 100) / Total);
}

public sealed class OtaResult
{
    public bool IsSuccess { get; }
    public string? Error { get; }

    private OtaResult(bool success, string? error)
    {
        IsSuccess = success;
        Error = error;
    }

    public static OtaResult Success() => new(true, null);
    public static OtaResult Fail(string err) => new(false, err);
}