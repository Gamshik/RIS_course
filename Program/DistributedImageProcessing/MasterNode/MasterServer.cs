using System.Net;
using System.Net.Sockets;
using Common.Messages;

namespace MasterNode
{
    /// <summary>
    /// Главный сервер, управляющий TCP-подключениями от клиентов и Slave-узлов.
    /// </summary>
    public class MasterServer
    {
        private readonly int _slavePort;
        private readonly int _clientPort;
        private readonly int _udpPort;
        private TcpListener _slaveListener;
        private TcpListener _clientListener;
        private readonly TaskScheduler _scheduler;
        private readonly ProgressSender _progressSender;
        private bool _isRunning;

        public MasterServer(int slavePort, int clientPort, int udpPort)
        {
            _slavePort = slavePort;
            _clientPort = clientPort;
            _udpPort = udpPort;
            _progressSender = new ProgressSender(udpPort);
            _scheduler = new TaskScheduler(_progressSender);
        }

        /// <summary>
        /// Запускает Master-сервер.
        /// </summary>
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _isRunning = true;

            // 1. Запуск слушателя для Slave-узлов
            _slaveListener = new TcpListener(IPAddress.Any, _slavePort);
            _slaveListener.Start();
            Console.WriteLine($"[MasterTCP] Сервер для Slave запущен на порту {_slavePort}...");

            // 2. Запуск слушателя для Клиентов
            _clientListener = new TcpListener(IPAddress.Any, _clientPort);
            _clientListener.Start();
            Console.WriteLine($"[MasterTCP] Сервер для Клиентов запущен на порту {_clientPort}...");

            // 3. Запуск основного цикла приема подключений
            Task slaveTask = ListenForSlavesAsync(cancellationToken);
            Task clientTask = ListenForClientsAsync(cancellationToken);

            try
            {
                await Task.WhenAny(slaveTask, clientTask);
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Master] Критическая ошибка сервера: {ex.Message}");
            }
            finally
            {
                Stop();
            }
        }

        /// <summary>
        /// Ожидает и обрабатывает подключения от Slave-узлов.
        /// </summary>
        private async Task ListenForSlavesAsync(CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _slaveListener.AcceptTcpClientAsync(cancellationToken);

                    // Создаем обработчик для нового Slave
                    SlaveHandler slaveHandler = new SlaveHandler(client, _scheduler);
                    _scheduler.AddSlave(slaveHandler);

                    // Запускаем асинхронное прослушивание Slave-узла
                    Task.Run(() => slaveHandler.StartListeningAsync(cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MasterTCP-Slave] Ошибка при приеме Slave: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Ожидает и обрабатывает подключения от Клиентов.
        /// </summary>
        private async Task ListenForClientsAsync(CancellationToken cancellationToken)
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _clientListener.AcceptTcpClientAsync(cancellationToken);

                    // Обрабатываем клиента в отдельном потоке
                    Task.Run(() => HandleClientConnectionAsync(client, cancellationToken));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MasterTCP-Client] Ошибка при приеме Клиента: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Обрабатывает входящее сообщение от клиента (задачу).
        /// </summary>
        private async Task HandleClientConnectionAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            NetworkStream stream = tcpClient.GetStream();
            var clientUdpEndpoint = new IPEndPoint(IPAddress.Loopback, 6000);

            try
            {
                // Читаем заголовок
                byte[] header = new byte[8];
                await ReadExactAsync(stream, header, 0, 8, cancellationToken);

                int typeInt = BitConverter.ToInt32(header, 0);
                int length = BitConverter.ToInt32(header, 4);

                if ((MessageType)typeInt != MessageType.ClientToMasterBatch) return;

                byte[] payload = new byte[length];
                await ReadExactAsync(stream, payload, 0, length, cancellationToken);

                var batch = MessageSerializer.DeserializeBatchRequest(Combine(header, payload), out _);

                Console.WriteLine($"[Master] Получен батч {batch.BatchId} — {batch.Images.Count} изображений");

                // Добавляем задачи — и всё! Больше НИЧЕГО не делаем в этом методе
                foreach (var img in batch.Images)
                {
                    var task = new ImageTask(img, stream, clientUdpEndpoint);
                    _scheduler.EnqueueBatch(batch.BatchId, task);
                }
            }
            catch (Exception ex)
            {
                // Только если клиент отвалился на этапе чтения батча
                Console.WriteLine($"[ClientHandler] Клиент отвалился при чтении батча: {ex.Message}");
                tcpClient.Close();
            }
        }

        private static byte[] Combine(byte[] a1, byte[] a2)
        {
            byte[] result = new byte[a1.Length + a2.Length];
            Buffer.BlockCopy(a1, 0, result, 0, a1.Length);
            Buffer.BlockCopy(a2, 0, result, a1.Length, a2.Length);
            return result;
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
                {
                    // Явный EOF — прерываем с исключением, чтобы не продолжать с частичными данными
                    throw new EndOfStreamException("Remote closed connection while reading data.");
                }
                totalRead += read;
            }
            return totalRead;
        }


        /// <summary>
        /// Останавливает Master-сервер.
        /// </summary>
        public void Stop()
        {
            if (_isRunning)
            {
                _isRunning = false;
                _slaveListener?.Stop();
                _clientListener?.Stop();
                _progressSender?.Close();
                Console.WriteLine("[Master] Master-сервер остановлен.");
            }
        }
    }
}