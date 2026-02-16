namespace ShuttleManager.Shared.Services.OtaUpdate
{
    public interface IOtaUpdateService
    {
        Task<OtaResult> RunAsync(string ip, string filePath, OtaTarget target, IProgress<OtaProgress>? progress, CancellationToken token);
    }
}