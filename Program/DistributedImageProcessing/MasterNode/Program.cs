namespace MasterNode
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("====================================");
            Console.WriteLine("        MASTER NODE - Координатор");
            Console.WriteLine("====================================\n");

            // Порты по умолчанию
            int slavePort = 5000;  // Для подключения Slave-узлов
            int clientPort = 5001; // Для подключения Клиентов (TCP)

            // Если передали аргументы командной строки
            if (args.Length >= 1 && int.TryParse(args[0], out int sPort))
            {
                slavePort = sPort;
            }
            if (args.Length >= 2 && int.TryParse(args[1], out int cPort))
            {
                clientPort = cPort;
            }

            Console.WriteLine($"Slave TCP порт: {slavePort}");
            Console.WriteLine($"Client TCP порт: {clientPort}");
            Console.WriteLine();

            // Создаём CancellationToken для корректной остановки
            CancellationTokenSource cts = new CancellationTokenSource();

            // Обработка Ctrl+C для корректного завершения
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n\nПолучен сигнал остановки...");
                cts.Cancel();
            };

            // Создаём и запускаем Master-сервер
            MasterServer server = new MasterServer(slavePort, clientPort);

            try
            {
                await server.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Работа Master-узла прервана пользователем.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка Master-узла: {ex.Message}");
            }
            finally
            {
                server.Stop();
            }

            Console.WriteLine("\nMaster-узел завершил работу.");
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}