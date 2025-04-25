using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AntColonyServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var delay = true;

            foreach (var arg in args)
            {
                if (arg.StartsWith("-delay"))
                {
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

            Console.WriteLine($"{new string('-', 42)}");
            Console.WriteLine($"Алгоритм муравьиной оптимизации (WebSocket)");
            Console.WriteLine($"{new string('-', 42)}");

            try
            {
                // Запуск админ-сервера (порт 3000)
                AdminServer.Initialize(new UriBuilder
                {
                    Scheme = "http",
                    Host = GetLocalIPAddress(false),
                    Port = 3000
                });

                // Запуск клиентского сервера (порт 3001)
                ClientServer.Initialize(new UriBuilder
                {
                    Scheme = "http",
                    Host = GetLocalIPAddress(false),
                    Port = 3001
                });

                var adminServer = new AdminServer();
                var clientServer = new ClientServer();

                // Запускаем оба сервера параллельно
                await Task.WhenAll(
                    adminServer.StartServer(),
                    clientServer.StartServer()
                );
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            if (delay) Console.ReadLine();
        }

        static string GetLocalIPAddress(bool isIPv6 = false)
        {
            string ip = string.Empty;
            foreach (var item in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
            {
                if (item.AddressFamily == AddressFamily.InterNetwork && !isIPv6)
                {
                    ip = item.ToString();
                    break;
                }
                else if (item.AddressFamily == AddressFamily.InterNetworkV6 && isIPv6)
                {
                    ip = item.ToString();
                    break;
                }
            }
            return ip;
        }
    }
}