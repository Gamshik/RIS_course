namespace SlaveNode
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("====================================");
            Console.WriteLine("   SLAVE NODE - Обработчик изображений");
            Console.WriteLine("====================================\n");

            string slaveName = "Slave-1";
            string masterHost = "127.0.0.1";
            int masterPort = 5000;

            if (args.Length >= 1)
            {
                slaveName = args[0];
            }
            if (args.Length >= 2)
            {
                masterHost = args[1];
            }
            if (args.Length >= 3)
            {
                if (int.TryParse(args[2], out int port))
                {
                    masterPort = port;
                }
            }

            Console.WriteLine($"Имя узла: {slaveName}");
            Console.WriteLine($"Master адрес: {masterHost}:{masterPort}");
            Console.WriteLine();

            // Создаём CancellationToken для корректной остановки
            CancellationTokenSource cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n\nПолучен сигнал остановки...");
                cts.Cancel();
            };

            SlaveWorker worker = new(slaveName, masterHost, masterPort);

            try
            {
                await worker.StartAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Работа прервана пользователем.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка: {ex.Message}");
            }

            Console.WriteLine("\nНажмите любую клавишу для выхода...");
            Console.ReadKey();
        }
    }
}