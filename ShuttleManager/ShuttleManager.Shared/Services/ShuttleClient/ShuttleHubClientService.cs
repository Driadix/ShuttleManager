using ShuttleManager.Shared.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShuttleManager.Shared.Services.ShuttleClient
{
    public class ShuttleHubClientService : IShuttleHubClientService, IDisposable
    {
        public event Action<string, string>? LogReceived;
        public event Action<string, int>? Connected;
        public event Action<string>? Disconnected;
        private readonly Dictionary<string, ShuttleConnection> _connections = [];
        private readonly object _lock = new();

        private class ShuttleConnection
        {
            public TcpClient? TcpClient { get; set; }
            public Stream? NetworkStream { get; set; }
            public StreamReader? Reader { get; set; }
            public StreamWriter? Writer { get; set; }
            public CancellationTokenSource? ReceiveCts { get; set; }
            public bool IsStartStream { get; set; } = false;
            public Task? ReceiveTask { get; set; }
            public int ShuttleId { get; set; } = -1;
            public string IpAddress { get; set; } = string.Empty;
            public readonly MemoryStream ReceiveBuffer = new();
        }

        public async Task<List<IPAddress>> ScanNetworkAsync(string baseIp, int startIp, int endIp, int port, int timeoutMs = 1000)
        {
            var foundDevices = new List<IPAddress>();
            var tasks = new List<Task>();

            //перебор подсети 
            for (int i = startIp; i <= endIp; i++)
            {
                string ip = $"{baseIp}.{i}";
                var task = Task.Run(async () =>
                {
                    try
                    {
                        using var client = new TcpClient();
                        var cts = new CancellationTokenSource(timeoutMs);
                        try
                        {
                            await client.ConnectAsync(IPAddress.Parse(ip), port, cts.Token);
                            if (client.Connected)
                            {
                                Debug.WriteLine("Старт TCP контакта для валидной точки входа.");
                                lock (foundDevices) foundDevices.Add(IPAddress.Parse(ip));
                            }
                        }
                        catch (OperationCanceledException) { }
                    }
                    catch (SocketException) { }
                });
                tasks.Add(task);
            }
            await Task.WhenAll(tasks);
            return foundDevices;
        }

        public List<Shuttle> GetConnectedShuttles()
        {
            lock (_lock)
            {
                var infos = new List<Shuttle>();
                foreach (var kvp in _connections)
                {
                    infos.Add(new Shuttle
                    {
                        IPAddress = kvp.Value.IpAddress,
                        IsConnected = kvp.Value.TcpClient?.Connected == true,
                    });
                }
                return infos;
            }
        }

        public ConnectedShuttleInfo? GetShuttleInfo(string ipAddress)
        {
            lock (_lock)
            {
                if (_connections.TryGetValue(ipAddress, out var conn))
                {
                    return new ConnectedShuttleInfo
                    {
                        IpAddress = conn.IpAddress,
                        IsConnected = conn.TcpClient?.Connected == true,
                        ShuttleId = conn.ShuttleId
                    };
                }
            }
            return null;
        }

        public async Task ConnectToShuttleAsync(string ipAddress, int port)
        {
            lock (_lock) {}

            var connection = new ShuttleConnection { IpAddress = ipAddress };

            try
            {
                Debug.WriteLine("Старт TCP контакта для прямого подключнения");
                OnLogReceived(connection.IpAddress, "start Message");
                connection.TcpClient = new TcpClient();
                await connection.TcpClient.ConnectAsync(ipAddress, port);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60); // 1 минут
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60); // 1 минута
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 1);
                connection.NetworkStream = connection.TcpClient.GetStream();
                connection.Writer = new StreamWriter(connection.NetworkStream) { AutoFlush = true };

                string? handshakeLine = await ReadLineAsync(connection);
                OnConnected(ipAddress, connection.ShuttleId);
             
                connection.ReceiveCts = new CancellationTokenSource();
                connection.ReceiveTask = Task.Run(async () => await ReceiveLoopAsync(connection, connection.ReceiveCts.Token), connection.ReceiveCts.Token);

                lock (_lock)
                {
                    _connections[ipAddress] = connection;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] Ошибка подключения к {ipAddress}: {ex.Message}");
                await InternalDisconnectAsync(ipAddress);
            }
        }

        private async Task<string?> ReadLineAsync(ShuttleConnection connection, CancellationToken cancellationToken = default)
        {
            if (!connection.IsStartStream)
            {
                connection.IsStartStream = true;
                return "start stream!";
            }

            const int BufferSize = 512;
            byte[] readBuffer = new byte[BufferSize];

            while (true)
            {
                byte[] currentBufferData = connection.ReceiveBuffer.ToArray();
                int newlineIndex = Array.IndexOf(currentBufferData, (byte)'\n');

                if (newlineIndex >= 0)
                {
                    string line = System.Text.Encoding.UTF8.GetString(currentBufferData, 0, newlineIndex);
                    connection.ReceiveBuffer.SetLength(0);
                    if (newlineIndex + 1 < currentBufferData.Length)
                    {
                        connection.ReceiveBuffer.Write(currentBufferData, newlineIndex + 1, currentBufferData.Length - newlineIndex - 1);
                    }
                    if (line.Length > 0 && line[^1] == '\r')
                        line = line[..^1];

                    return line;
                }
                int bytesRead = await connection.NetworkStream!.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    if (connection.ReceiveBuffer.Length == 0)
                        return null;

                    string partialLine = System.Text.Encoding.UTF8.GetString(connection.ReceiveBuffer.ToArray());
                    connection.ReceiveBuffer.SetLength(0);
                    if (partialLine.Length > 0 && partialLine[^1] == '\r')
                        partialLine = partialLine[..^1];
                    return partialLine;
                }

                connection.ReceiveBuffer.Write(readBuffer, 0, bytesRead);
                if (connection.ReceiveBuffer.Length > 4096)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Слишком длинная строка (>4KB), сброс буфера");
                    connection.ReceiveBuffer.SetLength(0);
                    return string.Empty;
                }
            }
        }

        private async Task ReceiveLoopAsync(ShuttleConnection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    string? line = await ReadLineAsync(connection, cancellationToken);

                    Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop: Получена строка от {connection.IpAddress}: '{line}'");

                    if (line == null)
                    {
                        Debug.WriteLine($"[ShuttleHubClientService] Соединение с {connection.IpAddress} закрыто сервером (получен null).");
                        await InternalDisconnectAsync(connection.IpAddress);
                        break;
                    }
                    OnLogReceived(connection.IpAddress, line);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop для {connection.IpAddress} отменён.");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Ошибка приёма от {connection.IpAddress}: {ex.Message}");
                    await InternalDisconnectAsync(connection.IpAddress);
                    break;
                }
            }
            Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop завершён для {connection.IpAddress}");
        }
        public async Task<bool> SendCommandToShuttleAsync(string ipAddress, string command, int timeoutMs = 1000)
        {
            ShuttleConnection? connection;
            lock (_lock)
            {
                if (!_connections.TryGetValue(ipAddress, out var conn))
                {
                    Debug.WriteLine($"[ShuttleHubClientService] SendCommandToShuttleAsync: Нет активного соединения для {ipAddress}.");
                    return false;
                }
                connection = conn;
            }
            return await SendCommandInternalAsync(connection, command, timeoutMs);
        }

        private async Task<bool> SendCommandInternalAsync(ShuttleConnection connection, string command, int timeoutMs)
        {
            if (connection.Writer == null)
            {
                Debug.WriteLine($"[ShuttleHubClientService] SendCommandInternalAsync: Нет активного соединения для {connection.IpAddress} (Writer null).");
                return false;
            }

            try
            {
                await connection.Writer.WriteLineAsync(command);

                var cts = new CancellationTokenSource(timeoutMs);
                connection.ReceiveBuffer.SetLength(0);
                await connection.Writer.WriteLineAsync(command);
                await connection.Writer.FlushAsync();
                var ackTask = ReadLineAsync(connection);
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);

                var completedTask = await Task.WhenAny(ackTask, delayTask);

                if (completedTask == delayTask)
                {
                    cts.Dispose();
                    Debug.WriteLine($"[ShuttleHubClientService] Таймаут ожидания подтверждения для команды: {command} от {connection.IpAddress}");
                    return false;
                }

                string? response = await ackTask;
                cts.Dispose();

                if (response == null)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Соединение с {connection.IpAddress} закрыто при ожидании подтверждения для: {command}");
                    await InternalDisconnectAsync(connection.IpAddress);
                    return false;
                }

                if (response.StartsWith($"ACK:{command.Split(':')[0]}"))
                {
                    Debug.WriteLine($"Debug ACK: {response}");
                    Debug.WriteLine($"[ShuttleHubClientService] ACK получен для: {command} от {connection.IpAddress}");
                    return true;
                }
                else if (response.StartsWith($"NACK:{command.Split(':')[0]}") || response.Contains("ACK:"))
                {
                    Debug.WriteLine($"Debug NACK: {response}");
                    Debug.WriteLine($"[ShuttleHubClientService] NACK получен для: {command} от {connection.IpAddress} - {response}");
                    return false;
                }
                else
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Неожиданный ответ при ожидании подтверждения для: {command} от {connection.IpAddress} - {response}. Потенциально старая прошивка");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] Ошибка отправки команды '{command}' к {connection.IpAddress}: {ex.Message}");
                await InternalDisconnectAsync(connection.IpAddress);
                return false;
            }
        }


        public void DisconnectFromShuttle(string ipAddress) => _ = InternalDisconnectAsync(ipAddress);

        private async Task InternalDisconnectAsync(string ipAddress)
        {
            ShuttleConnection? connectionToDispose = null;

            lock (_lock)
            {
                if (_connections.TryGetValue(ipAddress, out var connection))
                {
                    connectionToDispose = connection;
                    connection.ReceiveCts?.Cancel();
                    connection.ReceiveCts?.Dispose();
                    connection.ReceiveCts = null;

                    connection.Writer?.Close();
                    connection.Writer?.Dispose();
                    connection.Writer = null;

                    connection.Reader?.Close();
                    connection.Reader?.Dispose();
                    connection.Reader = null;

                    connection.NetworkStream?.Close();
                    connection.NetworkStream?.Dispose();
                    connection.NetworkStream = null;

                    connection.TcpClient?.Close();
                    connection.TcpClient = null;

                    _connections.Remove(ipAddress);
                }
            }


            if (connectionToDispose != null)
            {
                if (connectionToDispose.ReceiveTask != null)
                {
                    try
                    {
                        await connectionToDispose.ReceiveTask;
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                OnDisconnected(ipAddress);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var kvp in _connections)
                {
                    var conn = kvp.Value;
                    conn.ReceiveCts?.Cancel();
                    conn.ReceiveCts?.Dispose();
                    conn.Writer?.Dispose();
                    conn.Reader?.Dispose();
                    conn.NetworkStream?.Dispose();
                    conn.TcpClient?.Close();
                    conn.TcpClient?.Dispose();
                }
                _connections.Clear();
            }
        }
   
        private void OnConnected(string ip, int id) => Connected?.Invoke(ip, id);
        private void OnDisconnected(string ip) => Disconnected?.Invoke(ip);
        private void OnLogReceived(string ip, string log) => LogReceived?.Invoke(ip, log);
    }

     
}