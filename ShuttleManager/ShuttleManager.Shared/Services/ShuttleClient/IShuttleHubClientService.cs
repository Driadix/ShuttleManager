using ShuttleManager.Shared.Models;
using System.Net;
using System.Text.Json.Nodes;

namespace ShuttleManager.Shared.Services.ShuttleClient;


public class ConnectedShuttleInfo
{
    public string IpAddress { get; set; } = string.Empty;
    public int ShuttleId { get; set; } = -1;
    public bool IsConnected { get; set; } = false;
    public double BatteryVoltage { get; set; } = -1.0;
    public int BatteryPercentage { get; set; } = 0;
    public string Status { get; set; } = "Неизвестно";
    public int ErrorCode { get; set; } = -1;
    public int WarningCode { get; set; } = -1;
}

public interface IShuttleHubClientService
{  
    event Action<string, string>? LogReceived;        // Передаёт IP и лог
    event Action<string, int>? Connected;              // Передаёт IP и ID шаттла
    event Action<string>? Disconnected;                // Передаёт IP


    public Task ConnectToShuttleAsync(string ipAddress, int port);
    void DisconnectFromShuttle(string ipAddress);
    public Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs);
    List<Shuttle> GetConnectedShuttles();
    ConnectedShuttleInfo? GetShuttleInfo(string ipAddress);
    public Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000);
}