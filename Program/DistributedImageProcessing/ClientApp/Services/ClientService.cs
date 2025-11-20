using Common.Messages;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ClientApp.Services
{
    /// <summary>
    /// Сервис для управления сетевым взаимодействием с MasterNode.
    /// </summary>
    public class ClientService
    {
        private readonly IPEndPoint _masterTcpEndpoint;
        private const int ClientUdpPort = 6000;

        public event Action<string> ProgressUpdated;
        public event Action<ImageMessage> TaskCompleted;
        public event Action<string> ErrorOccurred;

        public ClientService(string masterHost, int masterTcpPort)
        {
            _masterTcpEndpoint = new IPEndPoint(IPAddress.Parse(masterHost), masterTcpPort);
        }
        public async Task SendTaskAndReceiveResultAsync(BatchRequestMessage batch, CancellationToken cancellationToken)
        {
            TcpClient tcpClient = new();
            NetworkStream stream = null;

            try
            {
                await tcpClient.ConnectAsync(_masterTcpEndpoint.Address, _masterTcpEndpoint.Port);
                stream = tcpClient.GetStream();

                byte[] batchData = MessageSerializer.SerializeBatchRequest(MessageType.ClientToMasterBatch, batch);
                await stream.WriteAsync(batchData, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                _ = ListenForProgressAsync(ClientUdpPort, cancellationToken);

                int expectedResults = batch.Images.Count;

                int received = 0;
                byte[] header = new byte[8];

                while (received < expectedResults && !cancellationToken.IsCancellationRequested)
                {
                    int readHeader = await ReadExactAsync(stream, header, 0, 8);
                    if (readHeader == 0)
                    {
                        break;
                    }
                    if (readHeader < 8)
                    {
                        throw new EndOfStreamException("Не удалось прочитать полный заголовок.");
                    }

                    int payloadLength = BitConverter.ToInt32(header, 4);
                    if (payloadLength < 0 || payloadLength > 50_000_000)
                        throw new Exception($"Некорректная длина payload: {payloadLength}");

                    byte[] payload = new byte[payloadLength];
                    int readPayload = await ReadExactAsync(stream, payload, 0, payloadLength);
                    if (readPayload < payloadLength)
                        throw new EndOfStreamException("Не удалось прочитать полный payload.");

                    byte[] full = new byte[8 + payloadLength];
                    Buffer.BlockCopy(header, 0, full, 0, 8);
                    Buffer.BlockCopy(payload, 0, full, 8, payloadLength);

                    var result = MessageSerializer.DeserializeImageMessage(full, out var type);
                    Debug.WriteLine($"DEBUG:type: {(int)type} ");
                    if (type == MessageType.MasterToClientResult)
                    {
                        TaskCompleted?.Invoke(result);
                        received++;
                        Debug.WriteLine($"DEBUG:received: {(int)received} ");
                    }
                }

            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
            finally
            {
                tcpClient.Close();
            }
        }

        private async Task ListenForProgressAsync(int localUdpPort, CancellationToken cancellationToken)
        {
            UdpClient udpClient = null;
            try
            {
                udpClient = new UdpClient(localUdpPort);
                udpClient.Client.ReceiveTimeout = 5000;

                while (!cancellationToken.IsCancellationRequested)
                {
                    UdpReceiveResult result;
                    try
                    {
                        result = await udpClient.ReceiveAsync();
                    }
                    catch (SocketException)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                        continue;
                    }

                    ProgressMessage progress = MessageSerializer.DeserializeProgressMessage(result.Buffer);

                    string statusText = progress.Status switch
                    {
                        0 => "В очереди",
                        1 => "Обрабатывается",
                        2 => "Завершено (100%)",
                        3 => "Ошибка",
                        _ => "Неизвестный статус"
                    };

                    string progressInfo = progress.Status == 2
                        ? $"[100% | Завершено]"
                        : progress.Status == 3
                            ? $"[ОШИБКА] {progress.Info}"
                            : $"[{statusText}] Детали: {progress.Info}";

                    ProgressUpdated?.Invoke(progressInfo);

                    if (progress.Status >= 2)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientService-UDP] Ошибка прослушивания UDP: {ex.Message}");
            }
            finally
            {
                udpClient?.Close();
            }
        }

        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead);

                if (read == 0)
                {
                    return totalRead;
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}