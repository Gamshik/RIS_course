using Common.Messages;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ClientApp.Services
{
    public class UpdateTaskStatusData
    {
        public string FileName { get; set; }
        public string StatusText { get; set; }
    }
    /// <summary>
    /// Сервис для управления сетевым взаимодействием с MasterNode.
    /// </summary>
    public class ClientService
    {
        private readonly IPEndPoint _masterTcpEndpoint;
        private const int ClientUdpPort = 6000;

        public event Action<UpdateTaskStatusData> ProgressUpdated;
        public event Action<ImageMessage> TaskCompleted;
        public event Action<string> ErrorOccurred;

        public ClientService(string masterHost, int masterTcpPort)
        {
            _masterTcpEndpoint = new IPEndPoint(IPAddress.Parse(masterHost), masterTcpPort);
        }
        public async Task SendBatchAndReceiveResultAsync(BatchRequestMessage batch, CancellationToken cancellationToken)
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

                var sw = Stopwatch.StartNew();

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

                    int messageType = BitConverter.ToInt32(header, 0);
                    int payloadLength = BitConverter.ToInt32(header, 4);
                    if (payloadLength < 0)
                        throw new Exception($"Некорректная длина payload: {payloadLength}");

                    byte[] payload = new byte[payloadLength];
                    int readPayload = await ReadExactAsync(stream, payload, 0, payloadLength);
                    if (readPayload < payloadLength)
                        throw new EndOfStreamException("Не удалось прочитать полный payload.");

                    var result = MessageSerializer.DeserializeImageMessage(payload, messageType, payloadLength);
    
                    if (messageType == (int)MessageType.MasterToClientResult && result.FileName != "ERROR")
                    {
                        TaskCompleted?.Invoke(result);
                        received++;
                    }
                }

                sw.Stop();
                Debug.WriteLine($"Время выполнения: {sw.ElapsedMilliseconds} мс");
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
            UdpClient ackClient = null;
            try
            {
                udpClient = new UdpClient(localUdpPort);
                ackClient = new UdpClient(); 
                udpClient.Client.ReceiveBufferSize = 1024 * 1024;

                while (!cancellationToken.IsCancellationRequested)
                {
                    UdpReceiveResult result = await udpClient.ReceiveAsync();

                    int seq = BitConverter.ToInt32(result.Buffer, 0);

                    byte[] msgData = new byte[result.Buffer.Length - 4];
                    Buffer.BlockCopy(result.Buffer, 4, msgData, 0, msgData.Length);

                    ProgressMessage progress = MessageSerializer.DeserializeProgressMessage(msgData);

                    byte[] ack = BitConverter.GetBytes(seq);
                    await ackClient.SendAsync(ack, ack.Length, result.RemoteEndPoint);

                    string statusText = progress.Status switch
                    {
                        0 => "В очереди",
                        1 => "Обрабатывается",
                        2 => "Завершено (100%)",
                        3 => "Ошибка",
                        _ => "Неизвестный статус"
                    };

                    ProgressUpdated?.Invoke(new UpdateTaskStatusData { FileName = progress.FileName, StatusText = statusText });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ClientService-UDP] Ошибка прослушивания UDP: {ex.Message}");
            }
            finally
            {
                udpClient?.Close();
                ackClient?.Close();
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