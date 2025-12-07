using System;

namespace Common.Messages
{
    /// <summary>
    /// Сообщение с изображением
    /// </summary>
    public class ImageMessage
    {
        /// <summary>
        /// Уникальный ID изображения
        /// </summary>
        public int ImageId { get; set; }

        /// <summary>
        /// Имя файла
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Ширина изображения в пикселях
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Высота изображения в пикселях
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Формат изображения (1 = PNG, 2 = JPEG, 3 = BMP)
        /// </summary>
        public int Format { get; set; }

        /// <summary>
        /// Байты изображения
        /// </summary>
        public byte[] ImageData { get; set; }

        public ImageMessage()
        {
            FileName = string.Empty;
            ImageData = Array.Empty<byte>();
        }

        public ImageMessage(int imageId, string fileName, int width, int height, int format, byte[] imageData)
        {
            ImageId = imageId;
            FileName = fileName ?? string.Empty;
            Width = width;
            Height = height;
            Format = format;
            ImageData = imageData ?? Array.Empty<byte>();
        }

        /// <summary>
        /// Возвращает размер сообщения в байтах
        /// </summary>
        public int GetSize()
        {
            // 4 (ImageId) + 4 (длина имени) + длина имени + 4 (Width) + 4 (Height) + 4 (Format) + длина данных
            return 4 + 4 + System.Text.Encoding.UTF8.GetByteCount(FileName) + 4 + 4 + 4 + 4 + ImageData.Length;
        }
    }
}