using System.Buffers;
namespace ShuttleManager.Shared.Intefraces;
public interface ITcpClientService
{
    /// <summary>
    /// Подключается к удалённому хосту по IP и порту.
    /// </summary>
    /// <param name="host">IP-адрес хоста.</param>
    /// <param name="port">Порт.</param>
    /// <returns>True, если подключение успешно.</returns>
    public Task<bool> ConnectAsync(string host, int port);

    /// <summary>
    /// Отправляет байты на подключённое устройство.
    /// </summary>
    /// <param name="data">Данные для отправки.</param>
    /// <returns>Задача.</returns>
    public Task SendAsync(ReadOnlyMemory<byte> data);

    /// <summary>
    /// Принимает фиксированное количество байт от устройства.
    /// </summary>
    /// <param name="length">Количество байт для чтения.</param>
    /// <returns>Последовательность байт.</returns>
    public Task<ReadOnlySequence<byte>> ReceiveAsync(int length);

    /// <summary>
    /// Принимает строку, заканчивающуюся символом новой строки (\n).
    /// </summary>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Строка или null, если соединение закрыто.</returns>
    Task<string?> ReceiveStringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Закрывает соединение.
    /// </summary>
    void Disconnect();
    //public Task<bool> ReconnectAsync(string host, int port, int maxRetries = 3);
    //bool IsConnected { get; }
}