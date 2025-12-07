using Common.Messages;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace MasterNode
{
    /// <summary>
    /// Планировщик задач. Управляет очередью, распределяет задачи по Round-Robin и обрабатывает результаты.
    /// </summary>
    public class TaskScheduler
    {
        private readonly ConcurrentQueue<ImageTask> _taskQueue = new ConcurrentQueue<ImageTask>();
        private readonly ConcurrentDictionary<int, ImageTask> _activeTasks = new ConcurrentDictionary<int, ImageTask>();
        private readonly List<SlaveHandler> _slaves = new List<SlaveHandler>();
        private int _nextSlaveIndex = 0;
        private readonly ProgressSender _progressSender;
        private readonly ConcurrentDictionary<long, NetworkStream> _batchClientStreams = new();
        private readonly ConcurrentDictionary<long, int> _batchRemaining = new();

        private readonly ConcurrentDictionary<int, long> _imageToBatchId = new();
        private readonly ConcurrentDictionary<long, int> _batchTotalImages = new();
        private readonly ConcurrentDictionary<long, int> _batchProcessedImages = new();

        public TaskScheduler(ProgressSender progressSender)
        {
            _progressSender = progressSender;
        }

        /// <summary>
        /// Добавляет батч в очередь.
        /// </summary>
        public void EnqueueBatch(long batchId, ImageTask task)
        {
            int currentRemaining = _batchRemaining.AddOrUpdate(batchId, 1, (_, old) => old + 1);
            int currentTotal = _batchTotalImages.AddOrUpdate(batchId, 1, (_, old) => old + 1);

            _batchProcessedImages.TryAdd(batchId, 0);

            _batchClientStreams.TryAdd(batchId, task.ClientStream);
            _imageToBatchId[task.ImageId] = batchId;
            _taskQueue.Enqueue(task);
            _activeTasks.TryAdd(task.ImageId, task);

            Console.WriteLine($"[Scheduler] Задача ID {task.ImageId} из батча {batchId} добавлена в очередь ({currentRemaining}/{currentTotal})");

            AssignNextTask();
        }

        /// <summary>
        /// Добавляет Slave-узел в список доступных.
        /// </summary>
        public void AddSlave(SlaveHandler slave)
        {
            lock (_slaves)
            {
                _slaves.Add(slave);
                Console.WriteLine($"[Scheduler] Slave {slave.SlaveId} подключен. Всего Slave-узлов: {_slaves.Count}");
            }
            AssignNextTask();
        }

        /// <summary>
        /// Удаляет Slave-узел из списка.
        /// </summary>
        public void RemoveSlave(SlaveHandler slave)
        {
            lock (_slaves)
            {
                _slaves.Remove(slave);
                Console.WriteLine($"[Scheduler] Slave {slave.SlaveId} отключен. Всего Slave-узлов: {_slaves.Count}");
            }
        }

        /// <summary>
        /// Запрашивает попытку назначения задачи. Вызывается после подключения Slave или получения результата.
        /// </summary>
        public void RequestTaskAssignment()
        {
            AssignNextTask();
        }

        /// <summary>
        /// Назначает следующую задачу по стратегии Round-Robin с учётом занятости Slave.
        /// </summary>
        private void AssignNextTask()
        {
            lock (_slaves)
            {
                if (_taskQueue.IsEmpty || _slaves.Count == 0)
                    return;

                int startIndex = _nextSlaveIndex;
                bool taskAssigned = false;

                do
                {
                    SlaveHandler slave = _slaves[_nextSlaveIndex];
                    _nextSlaveIndex = (_nextSlaveIndex + 1) % _slaves.Count;

                    Console.WriteLine($"[Scheduler] Slave-{slave.SlaveId} доступен - {slave.IsAvailable}");
                    if (slave.IsAvailable && _taskQueue.TryDequeue(out ImageTask task))
                    {
                        task.SetProcessing(slave);

                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await slave.SendTaskAsync(task);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Scheduler] Ошибка при назначении задачи на Slave {slave.SlaveId}: {ex}");
                                HandleTaskFailure(task, $"Задача ID {task.ImageId} не выполнена: Slave отключился или произошла ошибка.");
                            }
                        });

                        Console.WriteLine($"[Scheduler] Назначена задача ID {task.ImageId} узлу {slave.SlaveId}. Задач в очереди: {_taskQueue.Count}");

                        taskAssigned = true;
                        break; 
                    }

                } while (_nextSlaveIndex != startIndex);

                if (!taskAssigned)
                {
                    Console.WriteLine("[Scheduler] Все Slave заняты. Задача останется в очереди.");
                }
            }
        }

        /// <summary>
        /// Обновляет статус задачи и отправляет уведомление о прогрессе.
        /// </summary>
        public void UpdateTaskStatus(int imageId, int status, string info = "")
        {
            if (!_activeTasks.TryGetValue(imageId, out ImageTask task)) return;
            task.Status = status;

            if (_imageToBatchId.TryGetValue(imageId, out long batchId))
            {
                int total = _batchTotalImages.GetValueOrDefault(batchId, 0);
                int processed = _batchProcessedImages.GetValueOrDefault(batchId, 0);

                _progressSender.SendProgressAsync(task, total, processed, info);
            }
            else
            {
                int activeCount = _activeTasks.Count - _taskQueue.Count;
                _progressSender.SendProgressAsync(task, activeCount, info);
            }
        }

        /// <summary>
        /// Обрабатывает результат, полученный от Slave-узла.
        /// </summary>
        public async Task HandleTaskResult(ImageTask task, ImageMessage resultMessage) 
        {
            if (task == null) return;

            task.SetCompleted();
            if (_imageToBatchId.TryGetValue(task.ImageId, out long batchId))
                _batchProcessedImages.AddOrUpdate(batchId, 1, (k, v) => v + 1);

            Console.WriteLine($"[Scheduler] Задача ID {task.ImageId} завершена");

            await SendResultToClient(task, resultMessage);

            UpdateTaskStatus(task.ImageId, task.Status);
            _activeTasks.TryRemove(task.ImageId, out _);
            _imageToBatchId.TryRemove(task.ImageId, out _);
        }

        /// <summary>
        /// Отправляет результат клиенту.
        /// </summary>
        private async Task SendResultToClient(ImageTask task, ImageMessage resultMessage)
        {
            if (!_imageToBatchId.TryGetValue(task.ImageId, out long batchId))
                return;

            if (!_batchClientStreams.TryGetValue(batchId, out var stream) || stream == null || !stream.CanWrite)
            {
                Console.WriteLine($"[Result] Клиентский поток недоступен для батча {batchId}");
                return;
            }

            try
            {
                byte[] resultData = MessageSerializer.SerializeImageMessage(
                    MessageType.MasterToClientResult, resultMessage);

                await stream.WriteAsync(resultData, 0, resultData.Length);
                await stream.FlushAsync();  

                Console.WriteLine($"[Result] Отправлен результат ID {task.ImageId} (батч {batchId})");

                int remaining = _batchRemaining.AddOrUpdate(batchId, 0, (_, old) => old - 1);

                if (remaining == 0)
                {
                    Console.WriteLine($"[Master] Батч {batchId} полностью завершён. Закрываем TCP-соединение с клиентом.");

                    _batchClientStreams.TryRemove(batchId, out _);
                    _batchRemaining.TryRemove(batchId, out _);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Result] Ошибка отправки результата: {ex.Message}");
            }
        }

        /// <summary>
        /// Обрабатывает ошибку решения задачи.
        /// Отправляет клиенту сообщение об ошибке, 
        /// кидает задачу обратно в очередь для повторной обработки
        /// </summary>
        private void HandleTaskFailure(ImageTask task, string errorMessage)
        {
            task.SetError();
            Console.WriteLine($"[Scheduler] Ошибка задачи ID {task.ImageId}: {errorMessage}");

            UpdateTaskStatus(task.ImageId, task.Status, "Ошибка: " + errorMessage);
            _taskQueue.Enqueue(task);
        }
    }
}