using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management.Automation.Runspaces;
using System.Management.Automation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Net;
using System.Security;
using System.Text;

namespace AntColonyServer
{
    /// <summary>
    /// Алгоритм муравьиной оптимизации (Ant Colony Optimization)
    /// Разбиение задачи для нескольких клиентов
    /// </summary>
    public class ServerAnts
    {
        private readonly int _numClients;                       // Количество клиентов
        private readonly double _alpha;                         // Влияние феромонов
        private readonly double _beta;                          // Влияние эвристической информации
        private readonly double _RHO;                           // Коэффициент испарения феромонов
        private readonly int Q;                                 // Константа для обновления феромонов
        private readonly int _countSubjects;                    // Количество предметов
        private int _bestValue;
        private readonly int _maxIteration;                     // Количество итераций
        private double[] _pheromone;                            // «привлекательность» каждого элемента или пути для муравьев
        private ConcurrentDictionary<int, WebSocket> _clients;  // Словарь для клиентов сервера 
         
        private int _inPort;                                    // Порт для подключения 
        private readonly IPAddress _ipAddress;                  // Адрес сервера
        private readonly ServerConfig _serverConfig;            // Конфигурация алгоритма и сервера 
        private TcpListener _tcpClientListener;                 // Слушатель для порта 
        private TcpClient _tcpClient;                           // Сокет клиента 
        private int _clientCounter;                             // количество действительных клиентов
        NetworkStream _stream;                                  // Объект для работы с потоком сокета 


        public ServerAnts(IPAddress iPAddress, ServerConfig serverConfig)
        {
            if (serverConfig is not null)
            {

                if (iPAddress is not null)
                {
                    _ipAddress = iPAddress;
                }
                else
                {
                    throw new Exception("Необходимо указать IP-адрес!");
                }
                this._serverConfig = serverConfig;
                _numClients = serverConfig.NumClients;
                _alpha = serverConfig.Alpha;
                _beta = serverConfig.Beta;
                _RHO = serverConfig.RHO;
                Q = serverConfig.Q;
                _countSubjects = serverConfig.CountSubjects;
                _bestValue = 0;
                _maxIteration = serverConfig.MaxIteration;
                _inPort = serverConfig.InPort;
                _pheromone = Enumerable
                    .Repeat(1.0, _countSubjects)
                    .ToArray();
                _clients = new ConcurrentDictionary<int, WebSocket>();
                _clientCounter = -1;
            }
            else
            {
                throw new Exception("Проблема с загрузкой конфига!");
            }

        }
        /// <summary>
        /// Загрузка исходного набора данных, псевдослучайные числа
        /// </summary>
        /// <param name="countSubjects">кол-во предметов</param>
        /// <returns></returns>
        private (int[] values, int[] weights, int weightLimit) GenerateModelParameters(int countSubjects)
        {
            // Устанавливаем фиксированный seed для первого генератора
            Random random = new Random(42);

            // Генерация массивов значений и весов
            int[] values = new int[countSubjects];
            int[] weights = new int[countSubjects];
            for (int i = 0; i < countSubjects; i++)
            {
                values[i] = 100 + random.Next(1, 401); // Значения от 100 до 500
                weights[i] = 10 + random.Next(1, 91);  // Вес от 10 до 100
            }

            // Создаем новый seed на основе текущего времени
            long milliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            long scaledTime = milliseconds * 100;
            int seed = SumOfDigits(scaledTime);

            // Устанавливаем новый seed для случайных чисел
            random = new Random(seed);

            // Устанавливаем ограничение для веса
            int weightLimit = 500;

            return (values, weights, weightLimit);
        }

        private int SumOfDigits(long number)
        {
            int sum = 0;
            while (number != 0)
            {
                sum += (int)(number % 10);
                number /= 10;
            }
            return sum;
        }

        /// <summary>
        /// Метод для запуска сервера и алгоритма ACO
        /// </summary>
        /// <returns> Список предметов, лучшее значение, время запуска клиентов, время работы алгоритма</returns>
        public async Task<(List<int> bestItems, int bestValue, TimeSpan methodRunTimer, TimeSpan totalTime)> StartServer()
        {

            var (values, weights, weightLimit) = GenerateModelParameters(_countSubjects);
            var nAnts = NumberOfAntsPerClient(_serverConfig.MaxAnts, _numClients);
           

            var stopwatch = Stopwatch.StartNew();
            await InitListener(_inPort);
            stopwatch.Stop();

            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");

            for (int i = 0; i < _clients.Count; i++)
            {
                string message = await ReceiveData(i, 1024);
                if (message == "READY")
                {
                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{_alpha};{_beta};{nAnts[i]}";
                    await SendData(i, initData);
                }
            }

            int bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int i = 0; i < _clients.Count; i++)
            {
                var message = await ReceiveData(i, 1024);
                if (message != "READY")
                {
                    break;
                }
            }

            for (int iter = 1; iter < _maxIteration; iter++)
            {

                var (tmpBestValue, tmpBestItems, allValues, allItems) = await OneStepAntColony();

                if (tmpBestValue > bestValue)
                {
                    bestValue = tmpBestValue;
                    bestItems = tmpBestItems;
                }

                for (int i = 0; i < _pheromone.Length; i++)
                {
                    _pheromone[i] = (1.0 - _RHO) * _pheromone[i];
                }
                for (int k = 0; k < _serverConfig.MaxAnts; k++)
                {
                    double sumValues = allValues.Sum();
                    foreach (var itemIndex in allItems[k])
                    {
                        _pheromone[itemIndex] += Q / sumValues;
                    }
                }
            }

            for (int j = 0; j < _numClients; j++)
            {
                await SendData(j, "end");
            }

            stopwatch.Stop();
            TimeSpan methodRunTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Состав предметов: {string.Join(",", bestItems)}");
            Console.WriteLine($"--- Общая стоимость: {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");
            return (bestItems, bestValue, methodRunTimer, (clientStartTimer + methodRunTimer));

        }


        /// <summary>
        /// Создание слушателя для порта c проверкой на занятость порта
        /// </summary>
        /// <param name="port">номер порта</param>
        /// <returns></returns>
        private async Task InitListener(int port)
        {
            bool errorFlag = true;

            while (errorFlag)
            {
                try
                {
                    _tcpClientListener = new TcpListener(_ipAddress, port);
                    _tcpClientListener.Start();
                    errorFlag = false;
                    Console.WriteLine($"URI: ws://{_ipAddress.ToString()}:{port}");

                }
                catch (SocketException)
                {
                    _inPort++;
                    errorFlag = true;
                    continue;
                }
            }

            for (int i = 0; i < _numClients; i++)
            {
                Console.WriteLine ($"---> Waiting for connection...{i} of {_numClients}");
                _tcpClient = await _tcpClientListener.AcceptTcpClientAsync();

                _ = HandleClientAsync(_tcpClient);
                
            }
            Console.Write($"");

        }


        /// <summary>
        /// Подтверждение соединение клиентов 
        /// </summary>
        /// <param name="tcpClient"></param>
        /// <returns></returns>
        private async Task HandleClientAsync(TcpClient tcpClient)
        {
            int clientId = Interlocked.Increment(ref _clientCounter);
            Console.WriteLine($"---> Client {clientId} connected");

            _stream = tcpClient.GetStream();
            WebSocket webSocket = await UpgradeToWebSocketAsync(_stream);
            if (webSocket == null)
            {
                Console.WriteLine($"---> Client {clientId} did not complete WebSocket handshake");
                tcpClient.Close();
                return;
            }

            _clients[clientId] = webSocket;

        }

        /// <summary>
        /// Переключение TCPClient на WebSocket
        /// </summary>
        /// <param name="stream"></param>
        /// <returns>объект WebSocket для клиента</returns>
        private async Task<WebSocket> UpgradeToWebSocketAsync(NetworkStream stream)
        {
            byte[] buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (!request.Contains("Upgrade: websocket"))
            {
                return null;
            }

            string response = GenerateWebSocketHandshakeResponse(request);
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);

            return WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromMinutes(2));
        }

        /// <summary>
        /// Создание заголовка для подтверждения WebSocket
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private string GenerateWebSocketHandshakeResponse(string request)
        {
            string key = ExtractWebSocketKey(request);
            string acceptKey = Convert.ToBase64String(
                System.Security.Cryptography.SHA1.Create()
                .ComputeHash(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
            );

            return "HTTP/1.1 101 Switching Protocols\r\n" +
                   "Upgrade: websocket\r\n" +
                   "Connection: Upgrade\r\n" +
                   $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";
        }

        private string ExtractWebSocketKey(string request)
        {
            foreach (var line in request.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Sec-WebSocket-Key:"))
                {
                    return line.Split(':')[1].Trim();
                }
            }
            return string.Empty;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task<(int bestValue, List<int> bestItems, List<int> allValues, List<int[]> allItems)> OneStepAntColony()
        {

            for (int i = 0; i < _numClients; i++)
            {

                await SendData(i, string.Join(",", _pheromone));
            }

            List<int> bestValues = new List<int>();
            List<List<int>> bestItemsList = new List<List<int>>(_numClients);
            List<int> allValues = new List<int>();
            List<int[]> allItems = new List<int[]>();

            for (int i = 0; i < this._numClients; i++)
            {
                string response = await ReceiveData(i, 65000);
                var dataParts = response.Split(';');

                int parseInt = 0;
                int.TryParse(dataParts[0], out parseInt);
                int bestValueForClient = parseInt;
                var bestItemsForClient = dataParts[1]
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList();
                var allValuesForClient = dataParts[1]
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToList();
                var allItemsTemp = ParseAntSelections(dataParts[3]);

                // Добавляем значения, чтобы собрать данные от всех клиентов
                bestValues.Add(bestValueForClient);
                bestItemsList.Add(bestItemsForClient);
                allValues.AddRange(allValuesForClient);
                allItems.AddRange(allItemsTemp);
            }
            int maxIndex = bestValues.IndexOf(bestValues.Max());
            int bestValue = bestValues[maxIndex];
            List<int> bestItems = bestItemsList[maxIndex];

            return (bestValue, bestItems, allValues, allItems);
        }

        private List<int[]> ParseAntSelections(string input)
        {
            string[] antSelections = input
                .Split(',')
                .Select(s => s.Trim())
                .ToArray();

            List<int[]> result = antSelections
                .Select(antSelection => antSelection
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse)
                .ToArray())
                .ToList();
            return result;
        }


        private async Task SendData(int clientIndex, string message)
        {
            if (_clients.TryGetValue(clientIndex, out WebSocket webSocket) && webSocket.State == WebSocketState.Open)
            {
                byte[] responseBuffer = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(new ArraySegment<byte>(responseBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task<string> ReceiveData(int clientIndex, int countBuffer)
        {
            try
            {
                if (_clients.TryGetValue(clientIndex, out WebSocket webSocket) && webSocket.State == WebSocketState.Open)
                {
                    var buffer = new byte[countBuffer];
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        return Encoding.UTF8.GetString(buffer, 0, result.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.ToString());
            }
            return string.Empty;
        }

        private List<int> NumberOfAntsPerClient(int maxAnts, int numSock)
        {
            List<int> nAnts = new List<int>();
            int baseNumAnt = maxAnts / numSock;

            nAnts = Enumerable
                .Repeat(baseNumAnt, numSock)
                .ToList();

            int addAnt = maxAnts % numSock;
            for (int i = 0; i < addAnt; i++)
            {
                nAnts[i]++;
            }
            Console.WriteLine("Количество муравьев на клиенте: [");
            nAnts.ForEach(x => Console.Write(" " + x));
            Console.WriteLine("\n]");
            Console.WriteLine($"{new string('-', 32)}");
            return nAnts;
        }

        public void CloseServer()
        {
            // Удаляем клиента при завершении соединения
            foreach (var item in _clients)
            {
                _clients.TryRemove(item.Key, out _);
                Console.WriteLine($"---> Client {item.Key} disconnected");
            }
            Console.WriteLine("Server closed");
        }
    }


}
