using System.Net.Sockets;
using Common.Messages;

namespace SlaveNode
{
    /// <summary>
    /// Рабочий процесс Slave-узла
    /// </summary>
    public class SlaveWorker
    {
        private readonly string _slaveName;
        private readonly string _masterHost;
        private readonly int _masterPort;
        private readonly ImageProcessor _imageProcessor;
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isRunning;

        public SlaveWorker(string slaveName, string masterHost, int masterPort)
        {
            _slaveName = slaveName;
            _masterHost = masterHost;
            _masterPort = masterPort;
            _imageProcessor = new ImageProcessor(slaveName);
        }

        /// <summary>
        /// Запускает Slave-узел
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _isRunning = true;
            Console.WriteLine($"[{_slaveName}] Запуск Slave-узла...");

            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Подключаемся к Master
                    await ConnectToMasterAsync();

                    Console.WriteLine($"[{_slaveName}] Успешно подключен к Master ({_masterHost}:{_masterPort})");
                    Console.WriteLine($"[{_slaveName}] Ожидание задач...\n");

                    // Главный цикл обработки
                    await ProcessTasksAsync(cancellationToken);
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"[{_slaveName}] Ошибка подключения к Master: {ex.Message}");
                    Console.WriteLine($"[{_slaveName}] Повторная попытка через 5 секунд...\n");
                    await Task.Delay(5000, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{_slaveName}] Неожиданная ошибка: {ex.Message}");
                    await Task.Delay(2000, cancellationToken);
                }
                finally
                {
                    DisconnectFromMaster();
                }
            }

            Console.WriteLine($"[{_slaveName}] Slave-узел остановлен.");
        }

        /// <summary>
        /// Подключается к Master-узлу
        /// </summary>
        private async Task ConnectToMasterAsync()
        {
            _client = new TcpClient();
            _client.NoDelay = true;
            await _client.ConnectAsync(_masterHost, _masterPort);
            _stream = _client.GetStream();
        }

        /// <summary>
        /// Отключается от Master-узла
        /// </summary>
        private void DisconnectFromMaster()
        {
            _stream?.Close();
            _client?.Close();
            Console.WriteLine($"[{_slaveName}] Отключен от Master");
        }

        private async Task ProcessTasksAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    byte[] header = new byte[8];
                    int read = await ReadExactAsync(_stream, header, 0, 8, cancellationToken);
                    if (read == 0)
                    {
                        Console.WriteLine($"[{_slaveName}] Master закрыл соединение. Переподключаемся...");
                        break;
                    }

                    int messageType = BitConverter.ToInt32(header, 0);
                    int payloadLength = BitConverter.ToInt32(header, 4);

                    if (payloadLength < 0)
                        throw new Exception($"Некорректная длина payload: {payloadLength}");

                    
                    byte[] payload = new byte[payloadLength];
                    int readPayload = await ReadExactAsync(_stream, payload, 0, payloadLength, cancellationToken);
                    if (readPayload < payloadLength)
                    {
                        Console.WriteLine($"[{_slaveName}] Payload не полный");
                        break;
                    }

                    ImageMessage taskMessage = MessageSerializer.DeserializeImageMessage(payload, messageType, payloadLength);
                    ImageMessage result = _imageProcessor.ProcessImage(taskMessage);

                    byte[] resultData = MessageSerializer.SerializeImageMessage(MessageType.SlaveToMasterResult, result);
                    await _stream.WriteAsync(resultData, cancellationToken);
                    await _stream.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{_slaveName}] Ошибка обработки задачи: {ex.Message}");
                    DisconnectFromMaster();
                    break;
                }
            }
        }

        /// <summary>
        /// Читает точное количество байт из потока
        /// </summary>
        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);

                if (read == 0)
                    return totalRead;

                totalRead += read;
            }

            return totalRead;
        }
    }
}