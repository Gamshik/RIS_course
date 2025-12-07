using Common.ImageProcessing;
using System.Drawing;

namespace RobertsOperator_Tests
{
    public class RobertsOperatorTests
    {
        // 1. Null
        [Fact]
        public void ApplyRobertsOperatorParallel_ShouldThrow_OnNull()
        {
            Bitmap nullImage = null;
            Assert.Throws<ArgumentNullException>(() =>
            {
                RobertsOperator.ApplyRobertsOperatorParallel(nullImage);
            });
        }

        // 2. Размеры изображения
        [Fact]
        public void ApplyRobertsOperatorParallel_ShouldReturnSameSize()
        {
            using var bmp = new Bitmap(10, 15);
            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);
            Assert.Equal(10, result.Width);
            Assert.Equal(15, result.Height);
        }

        // 3. Маленькое изображение — чёрные края
        [Fact]
        public void ApplyRobertsOperatorParallel_SmallImage_CheckBlackBorders()
        {
            using var bmp = new Bitmap(3, 3);
            bmp.SetPixel(0, 0, Color.White);
            bmp.SetPixel(1, 0, Color.Black);
            bmp.SetPixel(2, 0, Color.Red);
            bmp.SetPixel(0, 1, Color.Green);
            bmp.SetPixel(1, 1, Color.Blue);
            bmp.SetPixel(2, 1, Color.Yellow);
            bmp.SetPixel(0, 2, Color.Gray);
            bmp.SetPixel(1, 2, Color.Purple);
            bmp.SetPixel(2, 2, Color.Orange);

            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            for (int x = 0; x < result.Width; x++)
            {
                var c = result.GetPixel(x, result.Height - 1);
                Assert.Equal(0, c.R);
                Assert.Equal(0, c.G);
                Assert.Equal(0, c.B);
            }

            for (int y = 0; y < result.Height; y++)
            {
                var c = result.GetPixel(result.Width - 1, y);
                Assert.Equal(0, c.R);
                Assert.Equal(0, c.G);
                Assert.Equal(0, c.B);
            }
        }

        // 4. Известный градиент для 2x2
        [Fact]
        public void ApplyRobertsOperatorParallel_CheckKnownGradient2x2()
        {
            using var bmp = new Bitmap(2, 2);
            bmp.SetPixel(0, 0, Color.White);
            bmp.SetPixel(1, 0, Color.White);
            bmp.SetPixel(0, 1, Color.Black);
            bmp.SetPixel(1, 1, Color.Black);

            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            var c = result.GetPixel(0, 0);
            Assert.Equal(255, c.R);
            Assert.Equal(255, c.G);
            Assert.Equal(255, c.B);
        }

        // 5. Одноцветное изображение
        [Theory]
        [InlineData(0, 0, 0)]     // чёрный
        [InlineData(255, 255, 255)] // белый
        public void ApplyRobertsOperatorParallel_SingleColor_NoGradient(int r, int g, int b)
        {
            using var bmp = new Bitmap(5, 5);
            for (int y = 0; y < 5; y++)
                for (int x = 0; x < 5; x++)
                    bmp.SetPixel(x, y, Color.FromArgb(r, g, b));

            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            // все пиксели кроме последней строки и столбца должны быть 0
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    var c = result.GetPixel(x, y);
                    Assert.Equal(0, c.R);
                    Assert.Equal(0, c.G);
                    Assert.Equal(0, c.B);
                }
            }
        }

        // 6. Чёрные границы
        [Fact]
        public void ApplyRobertsOperatorParallel_CheckBordersAreBlack()
        {
            using var bmp = new Bitmap(6, 6);
            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            for (int x = 0; x < 6; x++)
            {
                var c = result.GetPixel(x, 5); // последняя строка
                Assert.Equal(0, c.R);
            }
            for (int y = 0; y < 6; y++)
            {
                var c = result.GetPixel(5, y); // последний столбец
                Assert.Equal(0, c.R);
            }
        }

        // 7. Параллельная обработка больших изображений
        [Fact]
        public void ApplyRobertsOperatorParallel_LargeImage_ParallelDoesNotThrow()
        {
            int width = 200, height = 150;
            using var bmp = new Bitmap(width, height);
            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
        }

        // 8. Проверка последнего пикселя каждой строки
        [Fact]
        public void ApplyRobertsOperatorParallel_LastPixelOfEachRow_IsBlack()
        {
            using var bmp = new Bitmap(4, 4);
            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            for (int y = 0; y < 4; y++)
            {
                var c = result.GetPixel(3, y); // последний столбец
                Assert.Equal(0, c.R);
                Assert.Equal(0, c.G);
                Assert.Equal(0, c.B);
            }
        }

        // 9. Проверка последней строки
        [Fact]
        public void ApplyRobertsOperatorParallel_LastRow_IsBlack()
        {
            using var bmp = new Bitmap(4, 4);
            var result = RobertsOperator.ApplyRobertsOperatorParallel(bmp);

            for (int x = 0; x < 4; x++)
            {
                var c = result.GetPixel(x, 3); // последняя строка
                Assert.Equal(0, c.R);
                Assert.Equal(0, c.G);
                Assert.Equal(0, c.B);
            }
        }
    }
}