using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Sockets;

namespace AntColonyServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var delay = true;

            foreach (var arg in args) //-delay=no (без задержки)
            {
                if (arg.StartsWith("-delay"))
                {
                    // Если параметр содержит "-delay", проверяем следующее значение
                    var parts = arg.Split('='); 
                    if (parts.Length == 2 && parts[0] == "-delay")
                    {
                        if (parts[1].ToLower() == "no")
                        {
                            delay = false;
                        }
                        else if (parts[1].ToLower() == "yes")
                        {
                            delay = true;
                        }
                    }
                }
            }


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

                var storage = new SQLiteDatabase(dbFilePath);
                int testRunId = storage.AddTestRun(typeTest, DateTime.Now, false, typeTest);

                ServerAnts server = new ServerAnts(IPAddress.Parse(GetLocalIPAddress(false)), config);

                ShowConfig(config);
                AddConfigToStorage(testRunId, config, storage);

                try
                {
                    (List<int> bestItems, int bestValue, TimeSpan methodRunTimer, TimeSpan totalTime) = await server.StartServer();
                    storage.AddTestResult(testRunId, string.Join(",", bestItems), (double)bestValue, methodRunTimer.TotalSeconds, totalTime.TotalSeconds);

                    Console.WriteLine("\n");
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
        if(delay) Console.ReadLine();
        }

        static void AddConfigToStorage(int testRunId, ServerConfig serverConfig, SQLiteDatabase storage)
        {
            storage.AddTestParameter(testRunId, "Alpha", string.Format($"{serverConfig.Alpha}"));
            storage.AddTestParameter(testRunId, "Beta", string.Format($"{serverConfig.Beta}"));
            storage.AddTestParameter(testRunId, "Q", string.Format($"{serverConfig.Q}"));
            storage.AddTestParameter(testRunId, "RHO", string.Format($"{serverConfig.RHO}"));
            storage.AddTestParameter(testRunId, "CountSubjects", string.Format($"{serverConfig.CountSubjects}"));
            storage.AddTestParameter(testRunId, "maxIteration", string.Format($"{serverConfig.MaxIteration}"));
            storage.AddTestParameter(testRunId, "MaxAnts", string.Format($"{serverConfig.MaxAnts}"));
            storage.AddTestParameter(testRunId, "NumClients", string.Format($"{serverConfig.NumClients}"));


        }

        static void ShowConfig(ServerConfig serverConfig)
        {
            Console.WriteLine("{0,30}", "-----Конфигурация(config.json)-----");
            Console.WriteLine("{0,-30} {1}", "Количество компьютеров:", string.Join(", ", serverConfig.NumClients));
            Console.WriteLine("{0,-30} {1}", "Максимум муравьев:", serverConfig.MaxAnts);
            Console.WriteLine("{0,-30} {1}", "Максимум итераций:", serverConfig.MaxIteration);
            Console.WriteLine("{0,-30} {1}", "Alpha:", serverConfig.Alpha);
            Console.WriteLine("{0,-30} {1}", "Beta:", serverConfig.Beta);
            Console.WriteLine("{0,-30} {1}", "Q:", serverConfig.Q);
            Console.WriteLine("{0,-30} {1}", "RHO:", serverConfig.RHO);
            Console.WriteLine("{0,-30} {1}", "Количество предметов:", serverConfig.CountSubjects);
            Console.WriteLine($"{new string('-', 32)}");
        }

        static string GetLocalIPAddress(bool local)
        {
            if (local)
            {
                return "127.0.0.1";
            }
            else
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork) // IPv4
                    {
                        return ip.ToString();
                    }
                }

                return "127.0.0.1";
            }
        }
    }

}
