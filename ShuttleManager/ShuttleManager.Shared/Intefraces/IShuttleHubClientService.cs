using ShuttleManager.Shared.Models;
using System.Buffers.Text;
using System.Net;
using System.Text.Json.Nodes;

namespace ShuttleManager.Shared.Intefraces;


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
    // События для уведомления UI о изменениях
    event Action<string, JsonNode>? TelemetryReceived; // Передаёт IP и JSON
    event Action<string, string>? LogReceived;        // Передаёт IP и лог
    event Action<string, int>? Connected;              // Передаёт IP и ID шаттла
    event Action<string>? Disconnected;                // Передаёт IP

    // Методы управления пулом подключений
    /// <summary>
    /// Подключается к ESP32 на указанном IP и порту 3333.
    /// </summary>
    /// <param name="ipAddress">IP-адрес ESP32.</param>
    /// <returns>True, если подключение инициировано успешно.</returns>
    public Task<bool> ConnectToShuttleAsync(string ipAddress, int port);

    /// <summary>
    /// Отключается от ESP32 по указанному IP.
    /// </summary>
    /// <param name="ipAddress">IP-адрес ESP32.</param>
    void DisconnectFromShuttle(string ipAddress);

    /// <summary>
    /// Отправляет команду шаттлу по указанному IP и ожидает ACK или NACK.
    /// </summary>
    /// <param name="ipAddress">IP-адрес ESP32.</param>
    /// <param name="command">Команда (например, "LOAD", "SET_MAX_SPEED:50").</param>
    /// <param name="timeoutMs">Таймаут ожидания подтверждения.</param>
    /// <returns>True, если получен ACK, иначе False.</returns>
    public Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs);

    /// <summary>
    /// Получает список информации о всех подключённых шаттлах.
    /// </summary>
    /// <returns>Список информации о подключённых шаттлах.</returns>
    List<Shuttle> GetConnectedShuttles();

    /// <summary>
    /// Получает информацию о конкретном подключённом шаттле.
    /// </summary>
    /// <param name="ipAddress">IP-адрес ESP32.</param>
    /// <returns>Информация о шаттле или null, если не подключён.</returns>
    ConnectedShuttleInfo? GetShuttleInfo(string ipAddress);
    //ValueTask DisposeAsync();

    public Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000);
}