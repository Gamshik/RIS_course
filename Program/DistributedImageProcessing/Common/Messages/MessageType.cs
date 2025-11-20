namespace Common.Messages
{
    /// <summary>
    /// Типы сообщений в распределённой системе
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// Клиент отправляет изображение на Master
        /// </summary>
        ClientToMasterImage = 1,

        /// <summary>
        /// Master отправляет задачу Slave для обработки
        /// </summary>
        MasterToSlaveTask = 2,

        /// <summary>
        /// Slave отправляет результат обработки Master
        /// </summary>
        SlaveToMasterResult = 3,

        /// <summary>
        /// Master отправляет готовое изображение клиенту
        /// </summary>
        MasterToClientResult = 4,

        /// <summary>
        /// Master отправляет прогресс клиенту (UDP)
        /// </summary>
        MasterToClientProgress = 5,

        /// <summary>
        /// Клиент отправляет сразу набор изображений
        /// </summary>
        ClientToMasterBatch = 6
    }
}