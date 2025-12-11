using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace ShuttleManager.Shared.Models;

public class Shuttle
{
    public int ShuttleNumber { get; set; } = 0;
    public string? IPAddress { get; set; } = string.Empty;
    public int BatteryPercentage { get; set; } = 0;
    public bool IsAutoLogging { get; set; }
    public string? CurrentCommand { get; set; }
    public bool? Inverse { get; set; }
    public TcpClient? Client { get; }
    public bool LoggingStatus { get; set; }
    public bool IsFormOpened { get; set; }
    public int MaxSpeed { get; set; }
    public int InterPalleteDistance { get; set; }
    public int ShuttleLength { get; set; }
    public int BatteryLimit { get; set; }
    public double BatteryVoltage { get; set; } = 0.0;
    public bool IsConnected { get; set; } = false;
    public string? LastReceivedData { get; set; }
    public string CurrentStatus { get; set; } = "Неизвестно";
    public int ErrorCode { get; set; } = 0;
    public int WarningCode { get; set; } = 0;
    public DateTime ConnectionTime { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;




    // Новое: история сообщений для терминала
    private readonly ConcurrentQueue<string> _terminalMessages = new();
    private readonly object _lock = new object();

    public void AddTerminalMessage(string message)
    {
        lock (_lock)
        {
            _terminalMessages.Enqueue(message);

        }
    }

    public List<string> GetTerminalMessages()
    {
        lock (_lock)
        {
            return new List<string>(_terminalMessages);
        }
    }

    // Метод для обновления статуса соединения
    public void SetConnectionStatus(bool isConnected)
    {
        IsConnected = isConnected;
    }
}