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
    private const byte CMD_WRITE_STREAM = 0x05;

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
        _logger.LogInformation("Starting STM32 OTA update to {Ip} (Stream Optimized)", ip);

        using var client = new TcpClient();
        client.NoDelay = true;
        client.SendBufferSize = 64 * 1024;

        await client.ConnectAsync(ip, STM_PORT, token);
        using var stream = client.GetStream();

        // 1. INIT
        _logger.LogDebug("Sending CMD_INIT");
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);

        // 2. ERASE
        _logger.LogDebug("Sending CMD_ERASE (Waiting up to 60s...)");
        stream.ReadTimeout = 60000;
        await SendByte(stream, CMD_ERASE, token);
        await EnsureOk(stream, token);

        // 3. STREAM WRITE
        _logger.LogDebug("Sending CMD_WRITE_STREAM header");
        await SendByte(stream, CMD_WRITE_STREAM, token);

        var header = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), STM_BASE_ADDRESS);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)fw.Length);
        await stream.WriteAsync(header, token);

        stream.ReadTimeout = 5000;
        await EnsureOk(stream, token);

        _logger.LogDebug("Streaming firmware data...");

        const int progressChunkSize = 8192;
        int offset = 0;

        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();

            int len = Math.Min(progressChunkSize, fw.Length - offset);

            await stream.WriteAsync(fw.AsMemory(offset, len), token);

            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));
        }

        // 4. WAIT FOR COMPLETION
        _logger.LogDebug("Waiting for device processing...");
        stream.ReadTimeout = 30000;
        await EnsureOk(stream, token);

        // 5. RUN
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
            // Try to read a bit more if possible to see error, or just throw
            var hex = BitConverter.ToString(buffer);
            Console.WriteLine($"Received unexpected response: {hex}");
            throw new InvalidOperationException($"Device returned FAIL (0x{hex})");
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