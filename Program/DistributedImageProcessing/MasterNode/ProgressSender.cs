using System.Net.Sockets;
using Common.Messages;

namespace MasterNode
{
    public class ProgressSender
    {
        private readonly UdpClient _udpClient;

        public ProgressSender(int masterUdpPort)
        {
            _udpClient = new UdpClient();
            Console.WriteLine($"[MasterUDP] UDP отправитель готов");
        }

        /// <summary>
        /// Универсальный метод — сам считает processedImages
        /// </summary>
        public async Task SendProgressAsync(ImageTask task, int totalImages, int processedImages, string info = "")
        {
            var message = new ProgressMessage(
                task.ImageId,
                totalImages,
                processedImages,
                task.Status,
                info
            );

            try
            {
                byte[] data = MessageSerializer.SerializeProgressMessage(message);
                await _udpClient.SendAsync(data, data.Length, task.ClientUdpEndpoint);
                Console.WriteLine($"[MasterUDP] Прогресс ID {task.ImageId}: {processedImages}/{totalImages} → {task.ClientUdpEndpoint}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MasterUDP] Ошибка отправки прогресса: {ex.Message}");
            }
        }

        // ← Оставляем старую перегрузку для совместимости (одиночные задачи)
        public async Task SendProgressAsync(ImageTask task, int totalImages, string info = "")
        {
            int processed = task.Status == 2 ? 1 : 0;
            await SendProgressAsync(task, totalImages, processed, info);
        }

        public void Close()
        {
            try { _udpClient?.Close(); } catch { }
        }
    }
}