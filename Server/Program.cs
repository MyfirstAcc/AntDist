using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;

namespace AntColonyServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string dbFilePath = "testsAnts.db";
            var typeTest = "WebSocket";
            Console.WriteLine($"{new string('-', 42)}");
            Console.WriteLine($"Алгоритм муравьиной оптимизации ({typeTest})");
            Console.WriteLine($"{new string('-', 42)}");

            try
            {
                var configuration = new ConfigurationBuilder()
                              .SetBasePath(Directory.GetCurrentDirectory())
                              .AddJsonFile("config.json", optional: false, reloadOnChange: true)
                              .Build();

                var config = configuration.Get<ServerConfig>() ?? new ServerConfig();
                var nameClientsValue = configuration.GetSection("nameClients").Value;

                if (int.TryParse(nameClientsValue, out int clientCount))
                {

                    config.NameClients = new string[clientCount];
                    for (int i = 0; i < clientCount; i++)
                    {
                        config.NameClients[i] = $"{i + 1}"; // инициализируем пустыми строками
                    }
                }
                else
                {
                    // Если это массив, получаем его напрямую
                    config.NameClients = configuration.GetSection("nameClients").Get<string[]>();
                }

                var storage = new SQLiteDatabase(dbFilePath);
                int testRunId = storage.AddTestRun(typeTest, DateTime.Now, config.LocalTest,config.ProtocolType);

                ServerAnts server = new ServerAnts(IPAddress.Parse(GetLocalIPAddress()), config);
                ShowConfig(config);

                AddConfigToStorage(testRunId, config, storage);

                try
                {
                    (List<int> bestItems, int bestValue, TimeSpan methodRunTimer, TimeSpan totalTime) = await server.StartServer();
                    storage.AddTestResult(testRunId, string.Join(",", bestItems), (double)bestValue, methodRunTimer.TotalSeconds, totalTime.TotalSeconds);

                    Console.WriteLine("\n");
                    Console.ReadLine();
                }
                catch (Exception ex)
                {
                    throw new Exception(ex.Message);
                }
                finally
                {
                    server.CloseServer();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static void AddConfigToStorage(int testRunId, ServerConfig serverConfig, SQLiteDatabase storage)
        {
            storage.AddTestParameter(testRunId, "Alpha", string.Format($"{serverConfig.Alpha}"));
            storage.AddTestParameter(testRunId, "Beta", string.Format($"{serverConfig.Beta}"));
            storage.AddTestParameter(testRunId, "Q", string.Format($"{serverConfig.Q}"));
            storage.AddTestParameter(testRunId, "RHO", string.Format($"{serverConfig.RHO}"));
            storage.AddTestParameter(testRunId, "CountSubjects", string.Format($"{serverConfig.CountSubjects}"));
            storage.AddTestParameter(testRunId, "maxIteration", string.Format($"{serverConfig.maxIteration}"));
            storage.AddTestParameter(testRunId, "MaxAnts", string.Format($"{serverConfig.MaxAnts}"));
            storage.AddTestParameter(testRunId, "NumClients", string.Format($"{serverConfig.NameClients.Length}"));


        }

        static void ShowConfig(ServerConfig serverConfig)
        {
            Console.WriteLine("{0,30}", "-----Конфигурация(config.json)-----");
            Console.WriteLine("{0,-30} {1}", "Запуск локально:", serverConfig.LocalTest);
            Console.WriteLine("{0,-30} {1}", "Имена компьютеров:", string.Join(", ", serverConfig.NameClients));
            Console.WriteLine("{0,-30} {1}", "Максимум муравьев:", serverConfig.MaxAnts);
            Console.WriteLine("{0,-30} {1}", "Username:", serverConfig.Username);
            Console.WriteLine("{0,-30} {1}", "Password:", serverConfig.Password);
            Console.WriteLine("{0,-30} {1}", "Максимум итераций:", serverConfig.maxIteration);
            Console.WriteLine("{0,-30} {1}", "Alpha:", serverConfig.Alpha);
            Console.WriteLine("{0,-30} {1}", "Beta:", serverConfig.Beta);
            Console.WriteLine("{0,-30} {1}", "Q:", serverConfig.Q);
            Console.WriteLine("{0,-30} {1}", "RHO:", serverConfig.RHO);
            Console.WriteLine("{0,-30} {1}", "Количество предметов:", serverConfig.CountSubjects);
            Console.WriteLine("{0,-30} {1}", "Путь к exe на удаленном хосте:", serverConfig.PathToEXE);
            Console.WriteLine("{0,-30} {1}", "Имя файла:", serverConfig.NameFile);
            Console.WriteLine($"{new string('-', 32)}");
        }

        static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork) // IPv4
                {
                    return ip.ToString();
                }
            }
            throw new Exception("Локальный IP-адрес не найден!");
        }
    }

}
