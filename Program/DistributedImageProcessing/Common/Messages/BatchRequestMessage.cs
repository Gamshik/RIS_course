using System.Collections.Generic;

namespace Common.Messages
{
    /// <summary>
    /// Сообщение от клиента: "Вот тебе сразу несколько изображений, обработай их все"
    /// </summary>
    public class BatchRequestMessage
    {
        /// <summary>
        /// Уникальный ID батча (можно Guid или long)
        /// </summary>
        public long BatchId { get; set; }

        /// <summary>
        /// Список изображений в батче
        /// </summary>
        public List<ImageMessage> Images { get; set; } = new List<ImageMessage>();

        public BatchRequestMessage() { }

        public BatchRequestMessage(long batchId, List<ImageMessage> images)
        {
            BatchId = batchId;
            Images = images;
        }
    }
}