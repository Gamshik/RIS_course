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

                    // 2️⃣ Если мусор, двигаем окно по одному байту
                    while ((MessageType)messageType != MessageType.MasterToSlaveTask)
                    {
                        Console.WriteLine($"[{_slaveName}] Мусор в потоке: messageType={messageType}. Сдвигаем окно на 1 байт...");

                        // Сдвигаем header влево на 1 байт
                        Array.Copy(header, 1, header, 0, 7);

                        // Читаем новый байт в конец header
                        int r = await ReadExactAsync(_stream, header, 7, 1, cancellationToken);
                        if (r < 1)
                        {
                            Console.WriteLine($"[{_slaveName}] Не удалось прочитать новый байт, соединение потеряно");
                            throw new Exception("Поток повреждён");
                        }

                        // Снова вычисляем messageType
                        messageType = BitConverter.ToInt32(header, 0);
                        payloadLength = BitConverter.ToInt32(header, 4);
                    }


                    if (payloadLength < 0 || payloadLength > 100_000_000)
                        throw new Exception($"Некорректная длина payload: {payloadLength}");

                    Console.WriteLine($"[{_slaveName}] messageType: {messageType}. payloadLength: {payloadLength}");


                    // 2️⃣ Читаем payload целиком
                    byte[] payload = new byte[payloadLength];
                    int readPayload = await ReadExactAsync(_stream, payload, 0, payloadLength, cancellationToken);
                    if (readPayload < payloadLength)
                    {
                        Console.WriteLine($"[{_slaveName}] Payload не полный");
                        break;
                    }

                    // 3️⃣ Собираем полное сообщение
                    byte[] fullMessage = new byte[8 + payloadLength];
                    Buffer.BlockCopy(header, 0, fullMessage, 0, 8);
                    Buffer.BlockCopy(payload, 0, fullMessage, 8, payloadLength);

                    ImageMessage taskMessage = MessageSerializer.DeserializeImageMessage(fullMessage, out _);
                    ImageMessage result = _imageProcessor.ProcessImage(taskMessage);

                    // 5️⃣ Отправляем результат
                    byte[] resultData = MessageSerializer.SerializeImageMessage(MessageType.SlaveToMasterResult, result);
                    await _stream.WriteAsync(resultData, cancellationToken);
                    await _stream.FlushAsync();

                    //if ((MessageType)messageType != MessageType.MasterToSlaveTask)
                    //{
                    //    Console.WriteLine($"[{_slaveName}] Неизвестный тип сообщения: {messageType}");
                    //    Console.WriteLine($"[{_slaveName}] Заголовок (hex): {BitConverter.ToString(header)}");
                    //    // Дополнительно — прочитать payloadLength (если разумен) и вывести первые 32 байта полезной нагрузки для диагностики
                    //    if (payloadLength > 0 && payloadLength < 10_000_000)
                    //    {
                    //        try
                    //        {
                    //            byte[] preview = new byte[Math.Min(payloadLength, 32)];
                    //            int got = await ReadExactAsync(_stream, preview, 0, preview.Length, cancellationToken);
                    //            Console.WriteLine($"[{_slaveName}] Начало полезной нагрузки (hex): {BitConverter.ToString(preview, 0, got)}");
                    //        }
                    //        catch (Exception ex)
                    //        {
                    //            Console.WriteLine($"[{_slaveName}] Не удалось прочитать превью payload: {ex.Message}");
                    //        }
                    //    }
                    //    break;
                    //}

                    //Console.WriteLine($"[{_slaveName}] Тип сообщения: {messageType}");

                    //byte[] payload = new byte[payloadLength];
                    //await ReadExactAsync(_stream, payload, 0, payloadLength, cancellationToken);

                    //byte[] fullMessage = new byte[8 + payloadLength];
                    //Buffer.BlockCopy(header, 0, fullMessage, 0, 8);
                    //Buffer.BlockCopy(payload, 0, fullMessage, 8, payloadLength);

                    //ImageMessage taskMessage = MessageSerializer.DeserializeImageMessage(fullMessage, out _);

                    //ImageMessage resultMessage = _imageProcessor.ProcessImage(taskMessage);

                    //byte[] resultData = MessageSerializer.SerializeImageMessage(MessageType.SlaveToMasterResult, resultMessage);

                    //await _stream.WriteAsync(resultData, cancellationToken);
                    //await _stream.FlushAsync(cancellationToken);
                    
                    Console.WriteLine($"[{_slaveName}] Задача ID {taskMessage.ImageId} выполнена. Ожидаем следующие задачи...");
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
        /// ИСПРАВЛЕНО: Улучшена обработка ошибок
        /// </summary>
        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);

                if (read == 0)
                {
                    // Соединение закрыто корректно
                    return totalRead;
                }

                totalRead += read;
            }

            return totalRead;
        }
    }
}