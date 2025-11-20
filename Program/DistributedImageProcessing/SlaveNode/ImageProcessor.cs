using System.Drawing;
using System.Drawing.Imaging;
using Common.ImageProcessing;
using Common.Messages;

namespace SlaveNode
{
    /// <summary>
    /// Обработчик изображений на Slave-узле
    /// </summary>
    public class ImageProcessor
    {
        private readonly string _slaveName;

        public ImageProcessor(string slaveName)
        {
            _slaveName = slaveName;
        }

        /// <summary>
        /// Обрабатывает изображение, применяя оператор Робертса
        /// </summary>
        public ImageMessage ProcessImage(ImageMessage inputMessage)
        {
            try
            {
                Console.WriteLine($"[{_slaveName}] Начало обработки изображения ID: {inputMessage.ImageId}, имя: {inputMessage.FileName}");

                // Конвертируем байты в Bitmap
                Bitmap sourceImage = RobertsOperator.BytesToBitmap(inputMessage.ImageData);

                Console.WriteLine($"[{_slaveName}] Размер изображения: {sourceImage.Width}x{sourceImage.Height}");

                Bitmap processedImage;
                //if (sourceImage.Width * sourceImage.Height > 1000000) // Больше 1 мегапикселя
                //{
                //    Console.WriteLine($"[{_slaveName}] Используется быстрый алгоритм");
                //    processedImage = RobertsOperator.ApplyRobertsOperatorFast(sourceImage);
                //}
                //else
                //{
                //    Console.WriteLine($"[{_slaveName}] Используется стандартный алгоритм");
                //    processedImage = RobertsOperator.ApplyRobertsOperator(sourceImage);
                //}

                Console.WriteLine($"[{_slaveName}] Используется стандартный алгоритм");
                processedImage = RobertsOperator.ApplyRobertsOperator(sourceImage);

                // Определяем формат для сохранения
                ImageFormat format = GetImageFormat(inputMessage.Format);

                // Конвертируем обратно в байты
                byte[] resultBytes = RobertsOperator.BitmapToBytes(processedImage, format);

                Console.WriteLine($"[{_slaveName}] Обработка завершена. Размер результата: {resultBytes.Length} байт");

                // Создаём результирующее сообщение
                var resultMessage = new ImageMessage(
                    inputMessage.ImageId,
                    $"processed_{inputMessage.FileName}",
                    processedImage.Width,
                    processedImage.Height,
                    inputMessage.Format,
                    resultBytes
                );

                // Освобождаем ресурсы
                sourceImage.Dispose();
                processedImage.Dispose();

                return resultMessage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{_slaveName}] ОШИБКА при обработке изображения: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Получает ImageFormat по коду формата
        /// </summary>
        private ImageFormat GetImageFormat(int formatCode)
        {
            return formatCode switch
            {
                1 => ImageFormat.Png,
                2 => ImageFormat.Jpeg,
                3 => ImageFormat.Bmp,
                _ => ImageFormat.Png // По умолчанию PNG
            };
        }
    }
}