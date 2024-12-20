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
    /// Получение двунаправленного соединения, такой же эффект у TCPClient
    /// </summary>
    public class ServerAnts
    {
        private readonly int numClients;         // Количество клиентов
        private readonly double alpha;           // Влияние феромонов
        private readonly double beta;            // Влияние эвристической информации
        private readonly double RHO;             // Коэффициент испарения феромонов
        private readonly int Q;                  // Константа для обновления феромонов
        private readonly int countSubjects;      // Количество предметов
        private int bestValue;
        private readonly int maxIteration;       // Количество итераций
        private double[] pheromone;             // «привлекательность» каждого элемента или пути для муравьев
        private ConcurrentDictionary<int, WebSocket> clients;

        private int inPort;
        private IPAddress ipAddress;
        private ServerConfig serverConfig;
        private TcpListener tcpClientListener;
        private TcpClient tcpClient;
        private int clientCounter;
        NetworkStream stream;


        public ServerAnts(IPAddress iPAddress, ServerConfig serverConfig)
        {
            ipAddress = iPAddress;
            this.serverConfig = serverConfig;
            numClients = serverConfig.NameClients.Length == 0 ? 4 : serverConfig.NameClients.Length;
            alpha = serverConfig.Alpha;
            beta = serverConfig.Beta;
            RHO = serverConfig.RHO;
            Q = serverConfig.Q;
            countSubjects = serverConfig.CountSubjects;
            bestValue = 0;
            maxIteration = serverConfig.maxIteration;
            inPort = serverConfig.InPort;
            pheromone = Enumerable.Repeat(1.0, countSubjects).ToArray();
            clients = new ConcurrentDictionary<int, WebSocket>();
            clientCounter = -1;

        }
        /// <summary>
        /// Загрузка исходного набора данных
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

        /// Метод для суммирования цифр числа
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


        public async Task<(List<int> bestItems, int bestValue, TimeSpan methodRunTimer, TimeSpan totalTime)> StartServer()
        {

            var (values, weights, weightLimit) = GenerateModelParameters(countSubjects);
            var nAnts = NumberOfAntsPerClient(serverConfig.MaxAnts, numClients);
           

            var stopwatch = Stopwatch.StartNew();
            await InitListener(inPort);
            stopwatch.Stop();

            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");

            for (int i = 0; i < clients.Count; i++)
            {
                string message = await ReceiveData(i, 1024);
                if (message == "READY")
                {
                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{alpha};{beta};{nAnts[i]}";
                    await SendData(i, initData);
                }
            }

            int bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int i = 0; i < clients.Count; i++)
            {
                var message = await ReceiveData(i, 1024);
                if (message != "READY")
                {
                    break;
                }
            }

            for (int iter = 1; iter < maxIteration; iter++)
            {

                var (tmpBestValue, tmpBestItems, allValues, allItems) = await OneStepAntColony();

                if (tmpBestValue > bestValue)
                {
                    bestValue = tmpBestValue;
                    bestItems = tmpBestItems;
                }

                for (int i = 0; i < pheromone.Length; i++)
                {
                    pheromone[i] = (1.0 - RHO) * pheromone[i];
                }
                for (int k = 0; k < serverConfig.MaxAnts; k++)
                {
                    double sumValues = allValues.Sum();
                    foreach (var itemIndex in allItems[k])
                    {
                        pheromone[itemIndex] += Q / sumValues;
                    }
                }
            }

            for (int j = 0; j < numClients; j++)
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


        private async Task InitListener(int port)
        {
            bool errorFlag = true;


            while (errorFlag)
            {
                try
                {
                    tcpClientListener = new TcpListener(ipAddress, port);
                    tcpClientListener.Start();
                    errorFlag = false;
                    Console.WriteLine(ipAddress.ToString()+port);

                }
                catch (SocketException ex)
                {
                    inPort++;
                    errorFlag = true;
                    continue;
                }
            }


            for (int i = 0; i < numClients; i++)
            {
                Console.WriteLine("---> Waiting for connection...");
                tcpClient = await tcpClientListener.AcceptTcpClientAsync();

                // Обрабатываем соединение в новом потоке
                _ = HandleClientAsync(tcpClient);
            }
        }

        private async Task HandleClientAsync(TcpClient tcpClient)
        {
            int clientId = Interlocked.Increment(ref clientCounter);
            Console.WriteLine($"---> Client {clientId} connected");

            stream = tcpClient.GetStream();
            WebSocket webSocket = await UpgradeToWebSocketAsync(stream);
            if (webSocket == null)
            {
                Console.WriteLine($"---> Client {clientId} did not complete WebSocket handshake");
                tcpClient.Close();
                return;
            }

            clients[clientId] = webSocket;

        }



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
        /// 
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

            for (int i = 0; i < numClients; i++)
            {

                await SendData(i, string.Join(",", pheromone));
            }

            List<int> bestValues = new List<int>();
            List<List<int>> bestItemsList = new List<List<int>>(numClients);
            List<int> allValues = new List<int>();
            List<int[]> allItems = new List<int[]>();

            for (int i = 0; i < this.numClients; i++)
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
            string[] antSelections = input.Split(',').Select(s => s.Trim()).ToArray();
            List<int[]> result = antSelections.Select(antSelection => antSelection.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()).ToList();
            return result;
        }


        public async Task SendData(int clientIndex, string message)
        {
            if (clients.TryGetValue(clientIndex, out WebSocket webSocket) && webSocket.State == WebSocketState.Open)
            {
                byte[] responseBuffer = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(new ArraySegment<byte>(responseBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public async Task<string> ReceiveData(int clientIndex, int countBuffer)
        {
            try
            {
                if (clients.TryGetValue(clientIndex, out WebSocket webSocket) && webSocket.State == WebSocketState.Open)
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

            }
            return string.Empty;
        }

        private List<int> NumberOfAntsPerClient(int maxAnts, int numSock)
        {
            List<int> nAnts = new List<int>();
            int baseNumAnt = maxAnts / numSock;

            nAnts = Enumerable.Repeat(baseNumAnt, numSock).ToList();

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
            foreach (var item in clients)
            {
                clients.TryRemove(item.Key, out _);
                Console.WriteLine($"---> Client {item.Key} disconnected");
            }           
        }
    }


}
