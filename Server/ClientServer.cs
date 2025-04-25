using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AntColonyServer
{
    class ClientServer
    {
        static UriBuilder _uri;

        public static void Initialize(UriBuilder uri)
        {
            if (uri is not null)
            {
                _uri = uri;
            }
            else
            {
                throw new Exception("Необходимо указать IP-адрес и port!");
            }
        }

        public async Task StartServer()
        {
            Console.WriteLine("[LOG] Запуск клиентского сервера");
            Task httpTask = Task.Run(StartHttpServer);
            await Task.WhenAll(httpTask);
        }

        static async Task StartHttpServer()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(_uri.ToString());
            listener.Start();

            string rootDirectory = "wwwroot";
            Directory.CreateDirectory(rootDirectory);
            Directory.CreateDirectory(Path.Combine(rootDirectory, "client"));

            Console.WriteLine($"[LOG] Клиентский сервер запущен: {_uri}");
            Console.WriteLine($"[LOG] Клиентская часть: {_uri}client");

            while (true)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                string urlPath = request.Url.AbsolutePath.TrimStart('/');
                string filePath;

                try
                {
                    if (urlPath.StartsWith("client/") || urlPath == "client")
                    {
                        filePath = Path.Combine(rootDirectory, "client", urlPath == "client" ? "index.html" : urlPath.Substring(7));
                        await ServeFile(response, filePath);
                    }
                    else
                    {
                        filePath = Path.Combine(rootDirectory, "client", "index.html");
                        await ServeFile(response, filePath);
                    }
                }
                catch (Exception ex)
                {
                    response.StatusCode = 500;
                    byte[] errorBytes = Encoding.UTF8.GetBytes($"Ошибка: {ex.Message}");
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    Console.WriteLine($"[LOG] Ошибка обработки запроса: {ex.Message}");
                }
                response.OutputStream.Close();
            }
        }

        static async Task ServeFile(HttpListenerResponse response, string filePath)
        {
            if (File.Exists(filePath))
            {
                byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                response.ContentType = GetMimeType(filePath);
                response.ContentLength64 = fileBytes.Length;
                await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
            }
            else
            {
                response.StatusCode = 404;
                byte[] errorBytes = Encoding.UTF8.GetBytes("404 - файл не найден");
                await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
            }
        }

        static string GetMimeType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".ico" => "image/x-icon",
                _ => "application/octet-stream",
            };
        }
    }
}