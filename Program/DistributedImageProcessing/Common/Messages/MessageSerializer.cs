using System.Text;

namespace Common.Messages
{
    /// <summary>
    /// Сериализация и десериализация сообщений в бинарный формат
    /// Формат: [4 байта - тип][4 байта - длина][N байт - данные]
    /// </summary>
    public static class MessageSerializer
    {
        /// <summary>
        /// Сериализует ImageMessage в байты
        /// </summary>
        public static byte[] SerializeImageMessage(MessageType messageType, ImageMessage message)
        {
            using var ms = new MemoryStream();

            using var writer = new BinaryWriter(ms);

            writer.Write((int)messageType);

            int payloadSize = message.GetSize();
            writer.Write(payloadSize);

            writer.Write(message.ImageId);

            byte[] fileNameBytes = Encoding.UTF8.GetBytes(message.FileName);
            writer.Write(fileNameBytes.Length);
            writer.Write(fileNameBytes);

            writer.Write(message.Width);
            writer.Write(message.Height);
            writer.Write(message.Format);
            writer.Write(message.ImageData.Length);
            writer.Write(message.ImageData);

            return ms.ToArray();

        }

        /// <summary>
        /// Десериализует ImageMessage из байтов
        /// </summary>
        public static ImageMessage? DeserializeImageMessage(byte[] data, int messageType, int payloadSize)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            int imageId = reader.ReadInt32();

            int fileNameLength = reader.ReadInt32();
            byte[] fileNameBytes = reader.ReadBytes(fileNameLength);
            string fileName = Encoding.UTF8.GetString(fileNameBytes);
            
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int format = reader.ReadInt32();
            
            int imageDataLength = reader.ReadInt32();
            byte[] imageData = reader.ReadBytes(imageDataLength);
            
            return new ImageMessage(imageId, fileName, width, height, format, imageData);
        }

        /// <summary>
        /// Сериализует ProgressMessage в байты (для UDP)
        /// </summary>
        public static byte[] SerializeProgressMessage(ProgressMessage message)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                // Записываем тип сообщения
                writer.Write((int)MessageType.MasterToClientProgress);

                // Записываем данные (без длины, UDP пакет и так ограничен)
                writer.Write(message.ImageId);

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(message.FileName);
                writer.Write(fileNameBytes.Length);
                writer.Write(fileNameBytes);

                writer.Write(message.TotalImages);
                writer.Write(message.ProcessedImages);
                writer.Write(message.Status);

                byte[] infoBytes = Encoding.UTF8.GetBytes(message.Info);
                writer.Write(infoBytes.Length);
                writer.Write(infoBytes);

                return ms.ToArray();
            }
        }

        /// <summary>
        /// Десериализует ProgressMessage из байтов (из UDP)
        /// </summary>
        public static ProgressMessage DeserializeProgressMessage(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms))
            {
                reader.ReadInt32();

                int imageId = reader.ReadInt32();

                int fileNameLength = reader.ReadInt32();
                byte[] fileNameBytes = reader.ReadBytes(fileNameLength);
                string fileName = Encoding.UTF8.GetString(fileNameBytes);

                int totalImages = reader.ReadInt32();
                int processedImages = reader.ReadInt32();
                int status = reader.ReadInt32();

                int infoLength = reader.ReadInt32();
                byte[] infoBytes = reader.ReadBytes(infoLength);
                string info = Encoding.UTF8.GetString(infoBytes);

                return new ProgressMessage(imageId, totalImages, processedImages, status, fileName, info);
            }
        }

        /// <summary>
        /// Сериализует BatchRequestMessage
        /// </summary>
        public static byte[] SerializeBatchRequest(MessageType messageType, BatchRequestMessage batch)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

            writer.Write((int)messageType);
            writer.Write(0); 

            writer.Write(batch.BatchId);
            writer.Write(batch.Images.Count);

            foreach (var img in batch.Images)
            {
                writer.Write(img.ImageId);

                byte[] fileNameBytes = Encoding.UTF8.GetBytes(img.FileName ?? "");
                writer.Write(fileNameBytes.Length);
                writer.Write(fileNameBytes);

                writer.Write(img.Width);
                writer.Write(img.Height);
                writer.Write(img.Format);

                writer.Write(img.ImageData.Length);
                writer.Write(img.ImageData);
            }

            // Перезаписываем длину payload
            int payloadLength = (int)(ms.Length - 8);
            ms.Position = 4;
            writer.Write(payloadLength);

            return ms.ToArray();
        }


        /// <summary>
        /// Десериализует BatchRequestMessage
        /// </summary>
        public static BatchRequestMessage DeserializeBatchRequest(byte[] data, out MessageType messageType)
        {
            if (data == null || data.Length < 12) // минимум: type(4) + length(4) + batchId(8) + count(4)
                throw new InvalidDataException("Слишком короткий массив для десериализации");

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            messageType = (MessageType)reader.ReadInt32();
            int payloadLength = reader.ReadInt32();
            if (payloadLength < 0 || payloadLength > data.Length - 8)
                throw new InvalidDataException("Неверная длина payload");
        
            long batchId = reader.ReadInt64();
            int count = reader.ReadInt32();

            if (count < 0)
                throw new InvalidDataException("Неверное количество изображений");

            var images = new List<ImageMessage>(count);
            for (int i = 0; i < count; i++)
            {
                int imageId = reader.ReadInt32();

                int fileNameLength = reader.ReadInt32();
                if (fileNameLength < 0 || fileNameLength > 1024)
                    throw new InvalidDataException("Неверная длина имени файла");
                string fileName = Encoding.UTF8.GetString(ReadExact(reader, fileNameLength));

                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                int format = reader.ReadInt32();

                int dataLength = reader.ReadInt32();
                if (dataLength < 0)
                    throw new InvalidDataException("Неверная длина изображения");
                
                byte[] imageData = ReadExact(reader, dataLength);

                images.Add(new ImageMessage(imageId, fileName, width, height, format, imageData));
            }

            return new BatchRequestMessage(batchId, images);
        }

        private static byte[] ReadExact(BinaryReader reader, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;

            while (offset < count)
            {
                int read = reader.Read(buffer, offset, count - offset);
                if (read == 0)
                    throw new EndOfStreamException("Не удалось прочитать все данные");
                offset += read;
            }

            return buffer;
        }

    }
}