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
        CancellationToken token,
        bool fullErase = false)
    {
        if (!File.Exists(filePath))
            return OtaResult.Fail($"File not found {filePath}");

        if (Path.GetExtension(filePath).ToLower() != ".bin")
            return OtaResult.Fail("Only .bin supported");

        var firmware = await File.ReadAllBytesAsync(filePath, token);
        var stopWatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Initiating OTA Update for {Target} on {Ip}. File size: {Size} bytes. Full Erase: {FullErase}", target, ip, firmware.Length, fullErase);

            var result = target == OtaTarget.Stm32
                ? await RunStmAsync(ip, firmware, progress, token, fullErase)
                : await RunEspAsync(ip, firmware, progress, token);

            stopWatch.Stop();
            if (result.IsSuccess)
            {
                _logger.LogInformation("OTA Update Successful. Time elapsed: {Elapsed}s", stopWatch.Elapsed.TotalSeconds.ToString("F2"));
            }
            else
            {
                _logger.LogError("OTA Update Failed. Time elapsed: {Elapsed}s. Error: {Error}", stopWatch.Elapsed.TotalSeconds.ToString("F2"), result.Error);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return OtaResult.Fail("OTA Cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OTA Update Critical Exception");
            return OtaResult.Fail($"Exception: {ex.Message}");
        }
    }

    // ================= STM =================

    private async Task<OtaResult> RunStmAsync(
    string ip,
    byte[] fw,
    IProgress<OtaProgress>? progress,
    CancellationToken token,
    bool fullErase)
    {
        using var client = new TcpClient();
        client.NoDelay = true;

        await client.ConnectAsync(ip, STM_PORT, token);
        using var stream = client.GetStream();

        _logger.LogDebug("Sending CMD_INIT");
        stream.ReadTimeout = 2000;
        await SendByte(stream, CMD_INIT, token);
        await EnsureOk(stream, token);
        _logger.LogInformation("[STM] Bootloader Initialized.");

        // 2. ERASE
        string eraseMode = fullErase ? "MASS ERASE (Deleting Config)" : "Smart Erase (Preserving Config)";
        _logger.LogInformation("[STM] Sending CMD_ERASE ({Mode} - This may take 30-45s)...", eraseMode);
        stream.ReadTimeout = 60000;

        _logger.LogDebug("Sending CMD_ERASE (Waiting up to 60s...)");
        stream.ReadTimeout = 60000;
        await SendByte(stream, CMD_ERASE, token);
        // Send Erase Mode Byte: 0x01 = Full, 0x00 = Smart
        await SendByte(stream, fullErase ? (byte)0x01 : (byte)0x00, token);

        stream.ReadTimeout = 5000;
        int totalBlocks = (int)Math.Ceiling(fw.Length / 256.0);
        int offset = 0;

        byte[] packetBuffer = new byte[261];

        for (int i = 0; i < totalBlocks; i++)
        {
            token.ThrowIfCancellationRequested();

            packetBuffer[0] = CMD_WRITE;

            uint addr = STM_BASE_ADDRESS + (uint)offset;
            BinaryPrimitives.WriteUInt32LittleEndian(packetBuffer.AsSpan(1), addr);

            int len = Math.Min(256, fw.Length - offset);
            Buffer.BlockCopy(fw, offset, packetBuffer, 5, len);

            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));

            int percent = (int)((offset * 100) / fw.Length);
            if (percent - lastLogPercent >= 20) // Log every 20%
            {
                _logger.LogInformation("[STM] Uploading... {Percent}%", percent);
                lastLogPercent = percent;
            }
        }

        _logger.LogDebug("Sending CMD_RUN");

        await SendByte(stream, CMD_RUN, token);
        await EnsureOk(stream, token);

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
        client.NoDelay = true;
        client.SendBufferSize = 64 * 1024;

        _logger.LogInformation("[ESP] Connecting to {Ip}:{Port}...", ip, ESP_PORT);
        await client.ConnectAsync(ip, ESP_PORT, token);
        using var stream = client.GetStream();

        // 1. INIT
        _logger.LogInformation("[ESP] Sending CMD_INIT (Begin Update)...");
        stream.ReadTimeout = 5000;
        await SendByte(stream, CMD_INIT, token);

        var sizeBytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(sizeBytes, (uint)fw.Length);
        await stream.WriteAsync(sizeBytes, token);

        await EnsureOk(stream, token);

        // 2. STREAM COMMAND
        _logger.LogInformation("[ESP] Sending CMD_WRITE_STREAM (Turbo Mode)...");
        await SendByte(stream, CMD_WRITE_STREAM, token);

        await stream.WriteAsync(sizeBytes, token);
        await EnsureOk(stream, token);

        // 3. STREAM DATA
        _logger.LogInformation("[ESP] Streaming Firmware...");

        const int progressChunkSize = 8192;
        int offset = 0;
        int lastLogPercent = 0;

        while (offset < fw.Length)
        {
            token.ThrowIfCancellationRequested();

            int len = Math.Min(progressChunkSize, fw.Length - offset);
            await stream.WriteAsync(fw.AsMemory(offset, len), token);

            offset += len;
            progress?.Report(new OtaProgress(offset, fw.Length));

            int percent = (int)((offset * 100) / fw.Length);
            if (percent - lastLogPercent >= 20)
            {
                _logger.LogInformation("[ESP] Uploading... {Percent}%", percent);
                lastLogPercent = percent;
            }
        }

        // 4. WAIT FOR COMPLETION
        _logger.LogInformation("[ESP] Upload complete. Waiting for flash finish...");
        stream.ReadTimeout = 30000;
        await EnsureOk(stream, token);

        // 5. RUN
        _logger.LogInformation("[ESP] Sending CMD_RUN (Finalizing & Restarting)...");
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
            var hex = BitConverter.ToString(buffer);
            var err = (read == 0) ? "No Data / Disconnected" : $"0x{hex}";
            throw new InvalidOperationException($"Device returned FAIL or Unexpected Data: {err}");
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