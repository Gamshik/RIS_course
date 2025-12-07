namespace Common.Messages
{
    /// <summary>
    /// Сообщение о прогрессе обработки (для UDP)
    /// </summary>
    public class ProgressMessage
    {
        /// <summary>
        /// ID изображения
        /// </summary>
        public int ImageId { get; set; }

        /// <summary>
        /// название файла
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Общее количество изображений в очереди
        /// </summary>
        public int TotalImages { get; set; }

        /// <summary>
        /// Количество обработанных изображений
        /// </summary>
        public int ProcessedImages { get; set; }

        /// <summary>
        /// Статус обработки (0 = В очереди, 1 = Обрабатывается, 2 = Завершено)
        /// </summary>
        public int Status { get; set; }

        /// <summary>
        /// Дополнительная информация (название Slave, который обрабатывает)
        /// </summary>
        public string Info { get; set; }

        public ProgressMessage()
        {
            Info = string.Empty;
        }

        public ProgressMessage(int imageId, int totalImages, int processedImages, int status, string fileName, string info = "")
        {
            ImageId = imageId;
            FileName = fileName;
            TotalImages = totalImages;
            ProcessedImages = processedImages;
            Status = status;
            Info = info ?? string.Empty;
        }

        /// <summary>
        /// Возвращает процент выполнения
        /// </summary>
        public double GetProgressPercentage()
        {
            if (TotalImages == 0) return 0;
            return (double)ProcessedImages / TotalImages * 100;
        }
    }
}