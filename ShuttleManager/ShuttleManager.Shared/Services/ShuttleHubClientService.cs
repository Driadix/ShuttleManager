using ShuttleManager.Shared.Intefraces;
using ShuttleManager.Shared.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ShuttleManager.Shared.Services
{
    public class ShuttleHubClientService : IShuttleHubClientService, IDisposable
    {
        public event Action<string, JsonNode>? TelemetryReceived;
        public event Action<string, string>? LogReceived;
        public event Action<string, int>? Connected;
        public event Action<string>? Disconnected;

        private readonly Dictionary<string, ShuttleConnection> _connections = new();
        private readonly object _lock = new object(); // Защита доступа к _connections

        private class ShuttleConnection
        {
            public TcpClient? TcpClient { get; set; }
            public Stream? NetworkStream { get; set; }
            public StreamReader? Reader { get; set; }
            public StreamWriter? Writer { get; set; }
            public CancellationTokenSource? ReceiveCts { get; set; }
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

        public async Task<bool> ConnectToShuttleAsync(string ipAddress, int port)
        {
            lock (_lock)
            {
                if (_connections.ContainsKey(ipAddress))
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Уже подключено к {ipAddress}");
                    return false;
                }
            }

            var connection = new ShuttleConnection { IpAddress = ipAddress };

            try
            {
                Debug.WriteLine("Старт TCP контакта для прямого подключнения");
                connection.TcpClient = new TcpClient();
                await connection.TcpClient.ConnectAsync(ipAddress, port);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 60); // 1 минут
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 60); // 1 минута
                connection.TcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 1);
                connection.NetworkStream = connection.TcpClient.GetStream();
                connection.Writer = new StreamWriter(connection.NetworkStream) { AutoFlush = true };

                string? handshakeLine = await ReadLineAsync(connection); // Передаём NetworkStream
                //string? handshakeLine = await ReadLineFromStreamAsync(connection.NetworkStream); // Передаём NetworkStream
                if (string.IsNullOrEmpty(handshakeLine))
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Не получено приветствие от {ipAddress} или соединение закрыто. Старая прошивка");
                    //await InternalDisconnectAsync(ipAddress);
                    //return false;
                }

                //test start
                Debug.WriteLine($"[ShuttleHubClientService] Получено приветствие: '{handshakeLine}'"); // Отладочный вывод
                if (!handshakeLine.StartsWith("CONNECTED:SHUTTLE_HUB_V1:ID="))
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Неверное приветствие от {ipAddress}: {handshakeLine}. Старая версия прошивки.");

                    //await InternalDisconnectAsync(ipAddress);
                    //return false;
                }
                else
                {
                    var match = Regex.Match(handshakeLine, @"ID=(\d+)");
                    if (!match.Success || !int.TryParse(match.Groups[1].Value, out var parsedId))
                    {
                        Debug.WriteLine($"[ShuttleHubClientService] Не удалось извлечь или неверный ID из приветствия от {ipAddress}: {handshakeLine}");
                        await InternalDisconnectAsync(ipAddress);
                        return false;
                    }
                    connection.ShuttleId = parsedId; 
                }

                Debug.WriteLine($"[ShuttleHubClientService] Подключено к шаттлу ID: {connection.ShuttleId} по адресу {ipAddress}");
                OnConnected(ipAddress, connection.ShuttleId);

                if (!await SendCommandInternalAsync(connection, "STREAM_START", 2000))
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Не удалось запустить стрим от {ipAddress}. Старая версия прошивки или иная пробелма.");
                    //await InternalDisconnectAsync(ipAddress);
                    //return false;
                }

                Debug.WriteLine($"[ShuttleHubClientService] Стрим запущен для шаттла ID: {connection.ShuttleId} ({ipAddress})");

                connection.ReceiveCts = new CancellationTokenSource();
                connection.ReceiveTask = Task.Run(async () => await ReceiveLoopAsync(connection, connection.ReceiveCts.Token), connection.ReceiveCts.Token);

                lock (_lock)
                {
                    _connections[ipAddress] = connection;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ShuttleHubClientService] Ошибка подключения к {ipAddress}: {ex.Message}");
                await InternalDisconnectAsync(ipAddress);
                return false;
            }
        }

        private async Task<string?> ReadLineAsync(ShuttleConnection connection, CancellationToken cancellationToken = default)
        {
            const int BufferSize = 512;
            byte[] readBuffer = new byte[BufferSize];


            while (true)
            {
                // Ищем символ новой строки в уже имеющемся буфере
                byte[] currentBufferData = connection.ReceiveBuffer.ToArray();
                int newlineIndex = Array.IndexOf(currentBufferData, (byte)'\n');

                if (newlineIndex >= 0)
                {
                    // Нашли \n — извлекаем строку
                    string line = System.Text.Encoding.UTF8.GetString(currentBufferData, 0, newlineIndex);
                    // Удаляем из буфера всё до и включая \n
                    connection.ReceiveBuffer.SetLength(0);
                    if (newlineIndex + 1 < currentBufferData.Length)
                    {
                        connection.ReceiveBuffer.Write(currentBufferData, newlineIndex + 1, currentBufferData.Length - newlineIndex - 1);
                    }

                    // Убираем \r в конце, если есть (Windows-стиль)
                    if (line.Length > 0 && line[^1] == '\r')
                        line = line[..^1];

                    return line;
                }

                // \n не найден — читаем ещё данных
                int bytesRead = await connection.NetworkStream!.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                if (bytesRead == 0)
                {
                    // Соединение закрыто
                    if (connection.ReceiveBuffer.Length == 0)
                        return null; // Нет данных — конец потока

                    // Есть недочитанная строка без \n — возвращаем как есть
                    string partialLine = System.Text.Encoding.UTF8.GetString(connection.ReceiveBuffer.ToArray());
                    connection.ReceiveBuffer.SetLength(0);
                    if (partialLine.Length > 0 && partialLine[^1] == '\r')
                        partialLine = partialLine[..^1];
                    return partialLine;
                }

                // Добавляем прочитанное в буфер
                connection.ReceiveBuffer.Write(readBuffer, 0, bytesRead);

                // Защита от слишком большой строки (например, 10 КБ)
                if (connection.ReceiveBuffer.Length > 4096)
                {
                    Debug.WriteLine($"[ShuttleHubClientService] Слишком длинная строка (>4KB), сброс буфера");
                    connection.ReceiveBuffer.SetLength(0);
                    return string.Empty; // или null, или исключение
                }
            }
        }

        //private static async Task<string?> ReadLineFromStreamAsync(Stream stream)
        //{
        //    var sb = new System.Text.StringBuilder();
        //    byte[] buffer = new byte[1];
        //    int bytesRead;
        //    int totalRead = 0;
        //    while ((bytesRead = await stream.ReadAsync(buffer, 0, 1)) == 1)
        //    {
        //        totalRead++; 
        //        char c = (char)buffer[0];   
        //        if (c == '\n')
        //        {
        //            if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
        //            {
        //                sb.Length--; 
        //            }
        //            break;
        //        }
        //        sb.Append(c);            
        //        if (totalRead > 500)
        //        {
        //            Debug.WriteLine($"[ShuttleHubClientService] ReadLineFromStreamAsync: Превышен лимит чтения ({totalRead} байт).");
        //            break;
        //        }
        //    }
        //    string result = sb.ToString();
        //    if (bytesRead == 0 && sb.Length == 0)
        //    {
        //        Debug.WriteLine("[ShuttleHubClientService] ReadLineFromStreamAsync: Конец потока, строка пустая, возвращаем null"); // Отладка
        //        return null;
        //    }

        //    return result;
        //}

        private async Task ReceiveLoopAsync(ShuttleConnection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop: Ожидание данных от {connection.IpAddress}");
                try
                {
                    string? line = await ReadLineAsync(connection, cancellationToken);
                    //string? line = await ReadLineFromStreamAsync(connection.NetworkStream);
                    Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop: Получена строка от {connection.IpAddress}: '{line}'");

                    if (line == null)
                    {
                        Debug.WriteLine($"[ShuttleHubClientService] Соединение с {connection.IpAddress} закрыто сервером (получен null).");
                        await InternalDisconnectAsync(connection.IpAddress);
                        break;
                    }
                    OnLogReceived(connection.IpAddress, line);
                    const string markerTelemetry = "##TELEMETRY##:";
                    const string markerHeartBeat = "##HEARTBEAT##";
                    if (line.Contains(markerTelemetry))
                    {
                        int startIndex = line.IndexOf(markerTelemetry) + markerTelemetry.Length;
                        string jsonStr = line.Substring(startIndex);

                        try
                        {
                            var telemetry = JsonNode.Parse(jsonStr);
                            if (telemetry != null)
                            {
                                OnTelemetryReceived(connection.IpAddress, telemetry);
                            }
                        }
                        catch (Exception ex)
                        {
                            OnLogReceived(connection.IpAddress, $"[ERROR] Ошибка парсинга телеметрии: {ex.Message}, строка: {jsonStr}");
                        }
                    }
                    else if (line.Contains(markerHeartBeat))
                    {
                        OnLogReceived(connection.IpAddress, line);
                    }
                    else if (line.StartsWith("ACK:") || line.StartsWith("NACK:"))
                    {
                       
                        Debug.WriteLine($"[ShuttleHubClientService] Получен ответ для команды от {connection.IpAddress}: {line}");
                    }
                    else
                    {
                        OnLogReceived(connection.IpAddress, line);
                    }
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
            Debug.WriteLine($"[ShuttleHubClientService] ReceiveLoop завершён для {connection.IpAddress}"); // Отладка
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

                // Простая реализация ожидания ACK/NACK
                var cts = new CancellationTokenSource(timeoutMs);
                connection.ReceiveBuffer.SetLength(0);
                await connection.Writer.WriteLineAsync(command);
                await connection.Writer.FlushAsync();
                var ackTask = ReadLineAsync(connection);
                //var ackTask = ReadLineFromStreamAsync(connection.NetworkStream);
                var delayTask = Task.Delay(Timeout.Infinite, cts.Token);

                var completedTask = await Task.WhenAny(ackTask, delayTask);

                if (completedTask == delayTask)
                {
                    cts.Dispose();
                    // Таймаут
                    Debug.WriteLine($"[ShuttleHubClientService] Таймаут ожидания подтверждения для команды: {command} от {connection.IpAddress}");
                    return false;
                }

                string? response = await ackTask; // Получаем результат завершённой задачи
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
                    // Получили неожиданный ответ
                    Debug.WriteLine($"[ShuttleHubClientService] Неожиданный ответ при ожидании подтверждения для: {command} от {connection.IpAddress} - {response}");
                    return false;
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
        private void OnTelemetryReceived(string ip, JsonNode telemetry) => TelemetryReceived?.Invoke(ip, telemetry);
        private void OnLogReceived(string ip, string log) => LogReceived?.Invoke(ip, log);
    }

     
}