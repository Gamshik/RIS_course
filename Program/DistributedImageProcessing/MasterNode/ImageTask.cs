using System.Net;
using System.Net.Sockets;
using System.IO;
using Common.Messages;

namespace MasterNode
{
    /// <summary>
    /// Состояние обработки одного изображения.
    /// Используется для отслеживания задачи в системе Master.
    /// </summary>
    public class ImageTask
    {
        public ImageMessage TaskMessage { get; }
        public int ImageId => TaskMessage.ImageId;
        public string FileName => TaskMessage.FileName;

        /// <summary>
        /// Поток для отправки финального результата клиенту по TCP.
        /// (Клиент ждет ответа на том же соединении, по которому отправил запрос).
        /// </summary>
        public NetworkStream ClientStream { get; }

        /// <summary>
        /// UDP-адрес клиента для отправки сообщений о прогрессе.
        /// (Для простоты, будем считать, что клиентский UDP-порт = TCP-порт + 1)
        /// </summary>
        public IPEndPoint ClientUdpEndpoint { get; }

        /// <summary>
        /// Текущий статус обработки (0=В очереди, 1=Обрабатывается, 2=Завершено, 3=Ошибка)
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Slave-узел, который обрабатывает задачу (null, если в очереди)
        /// </summary>
        public SlaveHandler CurrentSlave { get; set; }

        public ImageTask(ImageMessage message, NetworkStream clientStream, IPEndPoint clientEndpoint)
        {
            TaskMessage = message;
            ClientStream = clientStream;

            ClientUdpEndpoint = new IPEndPoint(clientEndpoint.Address, 6000); 

            Status = 0; // В очереди
            CurrentSlave = null;
        }

        public void SetProcessing(SlaveHandler slave)
        {
            Status = 1; // Обрабатывается
            CurrentSlave = slave;
        }

        public void SetCompleted()
        {
            Status = 2; // Завершено
            CurrentSlave = null;
        }

        public void SetError()
        {
            Status = 3; // Ошибка
            CurrentSlave = null;
        }
    }
}