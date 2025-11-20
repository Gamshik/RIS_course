using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Common.ImageProcessing
{
    /// <summary>
    /// Реализация оператора Робертса для обнаружения границ
    /// </summary>
    public static class RobertsOperator
    {
        /// <summary>
        /// Применяет оператор Робертса к изображению
        /// </summary>
        /// <param name="sourceImage">Исходное изображение</param>
        /// <returns>Изображение с выделенными границами</returns>
        public static Bitmap ApplyRobertsOperator(Bitmap sourceImage)
        {
            if (sourceImage == null)
                throw new ArgumentNullException(nameof(sourceImage));

            int width = sourceImage.Width;
            int height = sourceImage.Height;

            // Создаём результирующее изображение
            Bitmap resultImage = new Bitmap(width, height);

            // Преобразуем в оттенки серого и применяем оператор
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    // Получаем 4 пикселя для ядер 2x2
                    Color p00 = sourceImage.GetPixel(x, y);
                    Color p01 = sourceImage.GetPixel(x + 1, y);
                    Color p10 = sourceImage.GetPixel(x, y + 1);
                    Color p11 = sourceImage.GetPixel(x + 1, y + 1);

                    // Преобразуем в оттенки серого (яркость)
                    int gray00 = (int)(p00.R * 0.299 + p00.G * 0.587 + p00.B * 0.114);
                    int gray01 = (int)(p01.R * 0.299 + p01.G * 0.587 + p01.B * 0.114);
                    int gray10 = (int)(p10.R * 0.299 + p10.G * 0.587 + p10.B * 0.114);
                    int gray11 = (int)(p11.R * 0.299 + p11.G * 0.587 + p11.B * 0.114);

                    // Применяем ядра Робертса
                    // Gx = | +1   0 |    Gy = |  0  +1 |
                    //      |  0  -1 |         | -1   0 |

                    int gx = gray00 - gray11;  // Диагональ ↘
                    int gy = gray01 - gray10;  // Диагональ ↙

                    // Вычисляем градиент: G = √(Gx² + Gy²)
                    int gradient = (int)Math.Sqrt(gx * gx + gy * gy);

                    // Ограничиваем значение диапазоном 0-255
                    gradient = Math.Min(255, Math.Max(0, gradient));

                    // Записываем результат (чёрно-белое изображение)
                    Color resultColor = Color.FromArgb(gradient, gradient, gradient);
                    resultImage.SetPixel(x, y, resultColor);
                }
            }

            // Заполняем последнюю строку и столбец чёрным (граничные пиксели)
            for (int x = 0; x < width; x++)
            {
                resultImage.SetPixel(x, height - 1, Color.Black);
            }
            for (int y = 0; y < height; y++)
            {
                resultImage.SetPixel(width - 1, y, Color.Black);
            }

            return resultImage;
        }

        /// <summary>
        /// Быстрая версия оператора Робертса с использованием LockBits (для больших изображений)
        /// </summary>
        public static Bitmap ApplyRobertsOperatorFast(Bitmap sourceImage)
        {
            if (sourceImage == null)
                throw new ArgumentNullException(nameof(sourceImage));

            int width = sourceImage.Width;
            int height = sourceImage.Height;

            Bitmap resultImage = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            // Блокируем биты для быстрого доступа
            BitmapData sourceData = sourceImage.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            BitmapData resultData = resultImage.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            unsafe
            {
                byte* sourcePtr = (byte*)sourceData.Scan0;
                byte* resultPtr = (byte*)resultData.Scan0;

                int stride = sourceData.Stride;

                for (int y = 0; y < height - 1; y++)
                {
                    for (int x = 0; x < width - 1; x++)
                    {
                        // Получаем указатели на 4 пикселя
                        byte* p00 = sourcePtr + y * stride + x * 3;
                        byte* p01 = sourcePtr + y * stride + (x + 1) * 3;
                        byte* p10 = sourcePtr + (y + 1) * stride + x * 3;
                        byte* p11 = sourcePtr + (y + 1) * stride + (x + 1) * 3;

                        // Преобразуем в оттенки серого (BGR формат)
                        int gray00 = (int)(p00[2] * 0.299 + p00[1] * 0.587 + p00[0] * 0.114);
                        int gray01 = (int)(p01[2] * 0.299 + p01[1] * 0.587 + p01[0] * 0.114);
                        int gray10 = (int)(p10[2] * 0.299 + p10[1] * 0.587 + p10[0] * 0.114);
                        int gray11 = (int)(p11[2] * 0.299 + p11[1] * 0.587 + p11[0] * 0.114);

                        // Применяем ядра Робертса
                        int gx = gray00 - gray11;
                        int gy = gray01 - gray10;

                        // Вычисляем градиент
                        int gradient = (int)Math.Sqrt(gx * gx + gy * gy);
                        gradient = Math.Min(255, Math.Max(0, gradient));

                        // Записываем результат (BGR)
                        byte* result = resultPtr + y * stride + x * 3;
                        result[0] = result[1] = result[2] = (byte)gradient;
                    }
                }
            }

            sourceImage.UnlockBits(sourceData);
            resultImage.UnlockBits(resultData);

            return resultImage;
        }

        /// <summary>
        /// Конвертирует байты в Bitmap
        /// </summary>
        public static Bitmap BytesToBitmap(byte[] imageBytes)
        {
            using (var ms = new System.IO.MemoryStream(imageBytes))
            {
                return new Bitmap(ms);
            }
        }

        /// <summary>
        /// Конвертирует Bitmap в байты
        /// </summary>
        public static byte[] BitmapToBytes(Bitmap image, ImageFormat format)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                image.Save(ms, format);
                return ms.ToArray();
            }
        }
    }
}