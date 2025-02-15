using System.Diagnostics;
using System.Net;
using System.Text;

namespace AntColonyServer
{

    /// <summary>
    /// Простой HTTP-сервер. Выдача клиентской части: index страницы и js файла
    /// </summary>
    class HttpAntServer
    {
        static UriBuilder _uri;
        static int _numClients;
        public static void Initialize(UriBuilder uri, int numClients)
        {
            if (uri is not null)
            {
                _uri = uri;
            }
            else
            {
                throw new Exception("Необходимо указать IP-адрес и port!");
            }

            if (numClients > 0)
            {
                _numClients = numClients;
            }
        }

        public async Task startServer()
        {
            Task httpTask = Task.Run(StartHttpServer);
            await Task.WhenAll(httpTask);
        }

        /// <summary>
        /// Выдача статических данных по HTTP запросу
        /// </summary>
        /// <returns></returns>
        static async Task StartHttpServer()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(_uri.ToString());
            listener.Start();

            string rootDirectory = "wwwroot"; // Папка с файлами
            Directory.CreateDirectory(rootDirectory); // Создаём, если её нет

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"---> HTTP-сервер запущен(URI): {_uri} <---");
            Console.ResetColor();
            while (true) // цикл прослушки для http запросов 
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                // Получаем путь к запрашиваемому файлу
                string urlPath = request.Url.AbsolutePath.TrimStart('/');
                string filePath = Path.Combine(rootDirectory, string.IsNullOrEmpty(urlPath) ? "index.html" : urlPath); //по умолчанию index страница

                if (File.Exists(filePath))
                {
                    // Если файл существует, читаем и возвращаем его
                    byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
                    response.ContentType = GetMimeType(filePath);
                    response.ContentLength64 = fileBytes.Length;
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }
                else
                {
                    // Если файл не найден, отдаём 404
                    response.StatusCode = 404;
                    byte[] errorBytes = Encoding.UTF8.GetBytes("404 - файл не найден");
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                }
                response.OutputStream.Close();
            }
        }

        /// <summary>
        /// Выбор типа файла
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Автоматизация для Chrome, запуск браузера локально для N клиентов
        /// </summary>
        static void StartChrome()
        {
            try
            {
                for (int i = 0; i < _numClients; i++)
                {
                    string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe"; // Путь к Chrome
                    string url = _uri.ToString();

                    // Проверяем, существует ли файл
                    if (File.Exists(chromePath))
                    {
                        // Запуск Chrome с URL
                        Process.Start(chromePath, url);
                        Console.WriteLine("Chrome открыт с адресом: " + url);
                    }
                    else
                    {
                        Console.WriteLine("Chrome не найден по пути: " + chromePath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при запуске Chrome: {ex.Message}");
            }
        }

    }
}
