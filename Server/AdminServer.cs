using System;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace AntColonyServer
{
    class AdminServer
    {
        static UriBuilder _uri;
        static ServerAnts _server;
        static SQLiteDatabase _storage;
        static ServerConfig _config;
        static bool _isRunning;
        static readonly string _dbFilePath = "testsAnts.db";
        static readonly string _typeTest = "WebSocket";
        static readonly ConcurrentBag<WebSocket> _webSockets = new ConcurrentBag<WebSocket>();

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

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("config.json", optional: false, reloadOnChange: true)
                .Build();

            _config = configuration.Get<ServerConfig>() ?? new ServerConfig();
            _storage = new SQLiteDatabase(_dbFilePath);
            _isRunning = false;
            LogEvent("Админ-сервер инициализирован").GetAwaiter().GetResult();
        }

        public async Task StartServer()
        {
            await LogEvent("Запуск админ-сервера");
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
            Directory.CreateDirectory(Path.Combine(rootDirectory, "admin"));

            await LogEvent($"Админ-сервер запущен: {_uri}");
            await LogEvent($"Админ-панель: {_uri}admin");

            while (true)
            {
                var context = await listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;

                string urlPath = request.Url.AbsolutePath.TrimStart('/');
                string filePath;

                try
                {
                    if (request.IsWebSocketRequest && urlPath == "ws")
                    {
                        await HandleWebSocketRequest(context);
                    }
                    else if (urlPath.StartsWith("admin/") || urlPath == "admin")
                    {
                        filePath = Path.Combine(rootDirectory, "admin", urlPath == "admin" ? "index.html" : urlPath.Substring(6));
                        await ServeFile(response, filePath);
                    }
                    else
                    {
                        response.StatusCode = 404;
                        byte[] errorBytes = Encoding.UTF8.GetBytes("404 - страница не найдена");
                        await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    }
                }
                catch (Exception ex)
                {
                    response.StatusCode = 500;
                    byte[] errorBytes = Encoding.UTF8.GetBytes($"Ошибка: {ex.Message}");
                    await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                    await LogEvent($"Ошибка обработки запроса: {ex.Message}");
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

        static async Task HandleWebSocketRequest(HttpListenerContext context)
        {
            var webSocketContext = await context.AcceptWebSocketAsync(null);
            var webSocket = webSocketContext.WebSocket;
            _webSockets.Add(webSocket);

            await LogEvent("WebSocket подключён (админ-панель)");

            try
            {
                var buffer = new byte[1024 * 4];
                while (webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessWebSocketMessage(webSocket, message);
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                await LogEvent($"Ошибка WebSocket (админ-панель): {ex.Message}");
            }
            finally
            {
                webSocket.Dispose();
                await LogEvent("WebSocket отключён (админ-панель)");
            }
        }

        static async Task ProcessWebSocketMessage(WebSocket webSocket, string message)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(message);
                var type = json.GetProperty("type").GetString();

                switch (type)
                {
                    case "get_config":
                        var configJson = JsonSerializer.Serialize(_config);
                        await SendWebSocketMessage(webSocket, new { type = "config", data = JsonSerializer.Deserialize<object>(configJson) });
                        await LogEvent("Отправлена конфигурация");
                        break;

                    case "set_config":
                        var newConfig = JsonSerializer.Deserialize<ServerConfig>(json.GetProperty("data").GetRawText());
                        if (newConfig != null)
                        {
                            _config = newConfig;
                            await SendWebSocketMessage(webSocket, new { type = "status", message = "Конфигурация обновлена" });
                            await LogEvent("Конфигурация обновлена");
                        }
                        else
                        {
                            await SendWebSocketMessage(webSocket, new { type = "error", message = "Неверный формат конфигурации" });
                        }
                        break;

                    case "start":
                        if (!_isRunning)
                        {
                            _isRunning = true;
                            _server = new ServerAnts(IPAddress.Parse(GetLocalIPAddress(false)), _config);

                            // Подписываемся на событие LogMessage
                            _server.LogMessage += async (sender, logMessage) => await LogEvent(logMessage);

                            Task.Run(async () =>
                            {
                                try
                                {
                                    await LogEvent("Запуск сервера муравьёв...");

                                    var result = await _server.StartServer();
                                    int testRunId = _storage.AddTestRun(_typeTest, DateTime.Now, false, _typeTest);
                                    _storage.AddTestResult(testRunId, string.Join(",", result.bestItems), (double)result.bestValue, result.methodRunTimer.TotalSeconds, result.methodRunTimer.TotalSeconds);
                                    AddConfigToStorage(testRunId, _config, _storage);
                                    await LogEvent($"Тест завершён: Лучшее значение = {result.bestValue}, Время = {result.totalTime.TotalSeconds}s");
                                    await BroadcastWebSocketMessage(new { type = "results", data = _storage.GetTestResults() });
                                }
                                catch (Exception ex)
                                {
                                    await LogEvent($"Ошибка при выполнении теста: {ex.Message}");
                                    await BroadcastWebSocketMessage(new { type = "error", message = ex.Message });
                                }
                                finally
                                {
                                    _server.CloseServer();
                                    _isRunning = false;
                                    await LogEvent("Сервер муравьёв остановлен");
                                }
                            });
                            await SendWebSocketMessage(webSocket, new { type = "status", message = "Сервер запущен" });
                            await LogEvent("Сервер запущен");
                        }
                        else
                        {
                            await SendWebSocketMessage(webSocket, new { type = "error", message = "Сервер уже работает" });
                        }
                        break;

                    case "stop":
                        if (_isRunning)
                        {
                            _server.CloseServer();
                            _isRunning = false;
                            await SendWebSocketMessage(webSocket, new { type = "status", message = "Сервер остановлен" });
                            await LogEvent("Сервер остановлен");
                        }
                        else
                        {
                            await SendWebSocketMessage(webSocket, new { type = "error", message = "Сервер не работает" });
                        }
                        break;

                    case "get_results":
                        var results = _storage.GetTestResults();
                        await SendWebSocketMessage(webSocket, new { type = "results", data = results });
                        await LogEvent("Отправлены результаты тестов");
                        break;

                    default:
                        await SendWebSocketMessage(webSocket, new { type = "error", message = "Неизвестный тип команды" });
                        break;
                }
            }
            catch (Exception ex)
            {
                await SendWebSocketMessage(webSocket, new { type = "error", message = $"Ошибка обработки команды: {ex.Message}" });
                await LogEvent($"Ошибка WebSocket: {ex.Message}");
            }
        }


        static async Task SendWebSocketMessage(WebSocket webSocket, object message)
        {
            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        static async Task BroadcastWebSocketMessage(object message)
        {
            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);
            foreach (var ws in _webSockets)
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        static async Task LogEvent(string message)
        {
            Console.WriteLine($"[LOG] {message}");
            await BroadcastWebSocketMessage(new { type = "log", message = $"{DateTime.Now}: {message}" });
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