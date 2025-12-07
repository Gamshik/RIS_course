using System.Net.Sockets;
using Common.Messages;

namespace MasterNode
{
    /// <summary>
    /// Обработчик одного Slave-узла.
    /// ИСПРАВЛЕНО: Синхронная модель - отправили задачу, ждём результат
    /// </summary>
    public class SlaveHandler
    {
        private readonly string _slaveId;
        private readonly NetworkStream _stream;
        private readonly TcpClient _client;
        private readonly TaskScheduler _scheduler;
        private ImageTask _currentTask;
        private readonly object _lock = new object();
        private bool _isDisconnected = false;

        public bool IsAvailable { get; private set; } = true;
        public string SlaveId => _slaveId;

        public SlaveHandler(TcpClient client, TaskScheduler scheduler)
        {
            _client = client;
            // ВАЖНО: Отключаем алгоритм Nagle
            _client.NoDelay = true;
            _stream = client.GetStream();
            _scheduler = scheduler;
            _slaveId = Guid.NewGuid().ToString().Substring(0, 8);

            Console.WriteLine($"[Slave-{_slaveId}] Установлено соединение от Slave: {_client.Client.RemoteEndPoint}");
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !_isDisconnected)
                {
                    await Task.Delay(1000, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[Slave-{_slaveId}] Прослушивание отменено.");
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Отправляет задачу Slave-узлу
        /// </summary>
        public async Task SendTaskAsync(ImageTask task)
        {
            lock (_lock)
            {
                if (!IsAvailable || _isDisconnected)
                {
                    throw new InvalidOperationException($"Slave {_slaveId} не доступен для приема новой задачи.");
                }

                _currentTask = task;
                IsAvailable = false;
            }

            try
            {
                Console.WriteLine($"[Master] Отправка задачи ID {task.ImageId} узлу - Slave-{_slaveId}...");

                byte[] taskData = MessageSerializer.SerializeImageMessage(
                    MessageType.MasterToSlaveTask,
                    task.TaskMessage);

                await _stream.WriteAsync(taskData, 0, taskData.Length);

                await _stream.FlushAsync();

                Console.WriteLine($"[Master] Задача ID {task.ImageId} отправлена узлу - Slave-{_slaveId} (размер: {taskData.Length} байт).");

                _scheduler.UpdateTaskStatus(task.ImageId, 1, _slaveId);

               await Task.Delay(1000);

                await ReceiveResultAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Master] Ошибка при отправке задачи или получении результата, узел - Slave-{_slaveId}: {ex.Message}");

                lock (_lock)
                {
                    _currentTask = null;
                    IsAvailable = true; 
                }

                Disconnect(); 
            }
        }

        /// <summary>
        /// Получает результат от Slave
        /// </summary>
        private async Task ReceiveResultAsync()
        {
            try
            {
                byte[] header = new byte[8];
                int bytesRead = await ReadExactAsync(_stream, header, 0, 8);

                if (bytesRead == 0)                    
                    throw new Exception("Slave закрыл соединение до отправки результата");

                if (bytesRead < 8)
                    throw new Exception($"Получен неполный заголовок ({bytesRead} байт)");

                int messageType = BitConverter.ToInt32(header, 0);
                int payloadLength = BitConverter.ToInt32(header, 4);

                Console.WriteLine($"[Master] Получение результата от Slave-{_slaveId}: размер {payloadLength} байт...");

                if ((MessageType)messageType != MessageType.SlaveToMasterResult)
                    throw new Exception($"Неожиданный тип сообщения {messageType} (ожидался {(int)MessageType.SlaveToMasterResult})");

                if (payloadLength < 0)
                    throw new Exception($"Недопустимый размер: {payloadLength} байт");

                byte[] payload = new byte[payloadLength];
                bytesRead = await ReadExactAsync(_stream, payload, 0, payloadLength);

                if (bytesRead < payloadLength)
                {
                    throw new Exception($"Получено {bytesRead} из {payloadLength} байт");
                }

                ImageMessage resultMessage = MessageSerializer.DeserializeImageMessage(payload, messageType, payloadLength);

                if (_currentTask == null || _currentTask.ImageId != resultMessage.ImageId)
                {
                    throw new Exception($"Получен результат ID {resultMessage.ImageId}, но ожидался {_currentTask?.ImageId ?? -1}");
                }

                Console.WriteLine($"[Master] Получен результат для ID {resultMessage.ImageId} от Slave-{_slaveId}.");

                await _scheduler.HandleTaskResult(_currentTask, resultMessage);

                lock (_lock)
                {
                    _currentTask = null;
                    IsAvailable = true;
                }

                _scheduler.RequestTaskAssignment();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Slave-{_slaveId}] Ошибка получения результата: {ex.Message}");
                lock (_lock)
                {
                    _currentTask = null;
                    IsAvailable = true;  
                }
            }
        }

        /// <summary>
        /// Читает точное количество байт из потока
        /// </summary>
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

        /// <summary>
        /// Разрывает соединение и уведомляет планировщик
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (_isDisconnected)
                    return;

                _isDisconnected = true;
            }

            try
            {
                _stream?.Close();
                _client?.Close();
                Console.WriteLine($"[Slave-{_slaveId}] Соединение разорвано.");
            }
            catch { /* Игнорируем ошибки при закрытии */ }

            // Уведомляем планировщик
            _scheduler.RemoveSlave(this);
        }
    }
}