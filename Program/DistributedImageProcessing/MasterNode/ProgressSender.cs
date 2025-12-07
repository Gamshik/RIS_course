using System.Net.Sockets;
using Common.Messages;

namespace MasterNode
{
    public class ProgressSender
    {
        private readonly ReliableUdpSenderWithQueue _sender;

        public ProgressSender()
        {
            _sender = new ReliableUdpSenderWithQueue();
        }

        public async Task SendProgressAsync(ImageTask task, int totalImages, int processedImages, string info = "")
        {
            var message = new ProgressMessage(
                task.ImageId,
                totalImages,
                processedImages,
                task.Status,
                task.FileName,
                info
            );

            try
            {
                byte[] data = MessageSerializer.SerializeProgressMessage(message);

                _sender.Enqueue(data, task.ClientUdpEndpoint);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterUDP] Ошибка отправки прогресса: {ex.Message}");
            }
        }

        public async Task SendProgressAsync(ImageTask task, int totalImages, string info = "")
        {
            int processed = task.Status == 2 ? 1 : 0;
            await SendProgressAsync(task, totalImages, processed, info);
        }

        public void Close()
        {
            try { _sender.Close(); } catch { }
        }
    }
}