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

        /// <summary>
        /// Главный цикл НЕ НУЖЕН! Slave работает синхронно:
        /// 1. Master отправляет задачу через SendTaskAsync
        /// 2. SendTaskAsync СРАЗУ ждёт результат
        /// 3. Получив результат, Slave снова становится доступным
        /// </summary>
        public async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            // Этот метод теперь просто ждёт, пока соединение не закроется
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
        /// Отправляет задачу Slave-узлу и СРАЗУ ждёт результат
        /// ИСПРАВЛЕНО: Синхронная модель запрос-ответ
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
                Console.WriteLine($"[Slave-{_slaveId}] Отправка задачи ID {task.ImageId}...");

                // ШАГ 1: Сериализуем и отправляем задачу
                byte[] taskData = MessageSerializer.SerializeImageMessage(
                    MessageType.MasterToSlaveTask,
                    task.TaskMessage);

                await _stream.WriteAsync(taskData, 0, taskData.Length);
                Console.WriteLine($"[Slave-{_slaveId}] Отправлены первые 8 байт задачи (hex): {BitConverter.ToString(taskData, 0, Math.Min(8, taskData.Length))}");
                Console.WriteLine($"[Slave-{_slaveId}] Отправлены первые 16 байт задачи (hex): {BitConverter.ToString(taskData, 0, Math.Min(16, taskData.Length))}");

                await _stream.FlushAsync();

                Console.WriteLine($"[Slave-{_slaveId}] Задача ID {task.ImageId} отправлена (размер: {taskData.Length} байт).");

                // Обновляем статус задачи
                _scheduler.UpdateTaskStatus(task.ImageId, 1, _slaveId);

                // ШАГ 2: СРАЗУ ждём результат от Slave
                await ReceiveResultAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Slave-{_slaveId}] Ошибка при отправке задачи или получении результата: {ex.Message}");

                lock (_lock)
                {
                    _currentTask = null;
                    IsAvailable = true; // Освобождаем Slave даже при ошибке
                }

                Disconnect(); 
            }
        }

        /// <summary>
        /// Получает результат от Slave
        /// НОВЫЙ МЕТОД: Вызывается сразу после отправки задачи
        /// </summary>
        private async Task ReceiveResultAsync()
        {
            try
            {
                // ШАГ 1: Читаем заголовок
                byte[] header = new byte[8];
                int bytesRead = await ReadExactAsync(_stream, header, 0, 8);

                if (bytesRead == 0)
                {
                    throw new Exception("Slave закрыл соединение до отправки результата");
                }

                if (bytesRead < 8)
                {
                    throw new Exception($"Получен неполный заголовок ({bytesRead} байт)");
                }

                // ШАГ 2: Парсим заголовок
                int messageType = BitConverter.ToInt32(header, 0);
                int payloadLength = BitConverter.ToInt32(header, 4);

                Console.WriteLine($"[Slave-{_slaveId}] Получение результата: размер {payloadLength} байт...");

                // ШАГ 3: Проверяем тип
                if ((MessageType)messageType != MessageType.SlaveToMasterResult)
                {
                    throw new Exception($"Неожиданный тип сообщения {messageType} (ожидался {(int)MessageType.SlaveToMasterResult})");
                }

                // ШАГ 4: Проверяем размер
                if (payloadLength < 0 || payloadLength > 100_000_000)
                {
                    throw new Exception($"Недопустимый размер: {payloadLength} байт");
                }

                // ШАГ 5: Читаем полезную нагрузку
                byte[] payload = new byte[payloadLength];
                bytesRead = await ReadExactAsync(_stream, payload, 0, payloadLength);

                if (bytesRead < payloadLength)
                {
                    throw new Exception($"Получено {bytesRead} из {payloadLength} байт");
                }

                // ШАГ 6: Собираем полное сообщение
                byte[] fullMessage = new byte[8 + payloadLength];
                Buffer.BlockCopy(header, 0, fullMessage, 0, 8);
                Buffer.BlockCopy(payload, 0, fullMessage, 8, payloadLength);

                // ШАГ 7: Десериализуем
                ImageMessage resultMessage = MessageSerializer.DeserializeImageMessage(fullMessage, out _);

                // ШАГ 8: Проверяем соответствие задачи
                if (_currentTask == null || _currentTask.ImageId != resultMessage.ImageId)
                {
                    throw new Exception($"Получен результат ID {resultMessage.ImageId}, но ожидался {_currentTask?.ImageId ?? -1}");
                }

                Console.WriteLine($"[Slave-{_slaveId}] Получен результат для ID {resultMessage.ImageId}.");

                // 1. СНАЧАЛА отправляем результат клиенту (пока _currentTask ещё не null!)
                await _scheduler.HandleTaskResult(_currentTask, resultMessage);

                // 2. ТЕПЕРЬ освобождаем Slave
                lock (_lock)
                {
                    _currentTask = null;
                    IsAvailable = true;
                }

                // 3. Запрашиваем следующую задачу
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