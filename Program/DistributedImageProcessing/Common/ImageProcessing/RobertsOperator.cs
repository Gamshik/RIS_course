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
        public static Bitmap ApplyRobertsOperatorParallel(Bitmap sourceImage)
        {
            if (sourceImage == null)
                throw new ArgumentNullException(nameof(sourceImage));

            int width = sourceImage.Width;
            int height = sourceImage.Height;

            Bitmap resultImage = new Bitmap(width, height);

            BitmapData srcData = sourceImage.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            BitmapData dstData = resultImage.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format24bppRgb);

            int strideSrc = srcData.Stride;
            int strideDst = dstData.Stride;

            unsafe
            {
                byte* srcPtr = (byte*)srcData.Scan0;
                byte* dstPtr = (byte*)dstData.Scan0;

                Parallel.For(0, height - 1, y =>
                {
                    byte* rowSrc = srcPtr + y * strideSrc;
                    byte* nextRowSrc = srcPtr + (y + 1) * strideSrc;
                    byte* rowDst = dstPtr + y * strideDst;

                    for (int x = 0; x < width - 1; x++)
                    {
                        // Получаем указатели на пиксели
                        byte* p00 = rowSrc + x * 3;
                        byte* p01 = rowSrc + (x + 1) * 3;
                        byte* p10 = nextRowSrc + x * 3;
                        byte* p11 = nextRowSrc + (x + 1) * 3;

                        int gray00 = (int)(p00[2] * 0.299 + p00[1] * 0.587 + p00[0] * 0.114);
                        int gray01 = (int)(p01[2] * 0.299 + p01[1] * 0.587 + p01[0] * 0.114);
                        int gray10 = (int)(p10[2] * 0.299 + p10[1] * 0.587 + p10[0] * 0.114);
                        int gray11 = (int)(p11[2] * 0.299 + p11[1] * 0.587 + p11[0] * 0.114);

                        int gx = gray00 - gray11;
                        int gy = gray01 - gray10;

                        int gradient = (int)Math.Sqrt(gx * gx + gy * gy);
                        if (gradient > 255) gradient = 255;
                        if (gradient < 0) gradient = 0;

                        // Запись в результирующее изображение
                        byte* pRes = rowDst + x * 3;
                        pRes[0] = pRes[1] = pRes[2] = (byte)gradient;
                    }

                    byte* pBlack = rowDst + (width - 1) * 3;
                    pBlack[0] = pBlack[1] = pBlack[2] = 0;
                });

                byte* lastRow = dstPtr + (height - 1) * strideDst;
                for (int x = 0; x < width; x++)
                {
                    byte* p = lastRow + x * 3;
                    p[0] = p[1] = p[2] = 0;
                }
            }

            sourceImage.UnlockBits(srcData);
            resultImage.UnlockBits(dstData);

            return resultImage;
        }

        /// <summary>
        /// Конвертирует байты в Bitmap
        /// </summary>
        public static Bitmap BytesToBitmap(byte[] imageBytes)
        {
            using (var ms = new MemoryStream(imageBytes))
            {
                return new Bitmap(ms);
            }
        }

        /// <summary>
        /// Конвертирует Bitmap в байты
        /// </summary>
        public static byte[] BitmapToBytes(Bitmap image, ImageFormat format)
        {
            using (var ms = new MemoryStream())
            {
                image.Save(ms, format);
                return ms.ToArray();
            }
        }
    }
}