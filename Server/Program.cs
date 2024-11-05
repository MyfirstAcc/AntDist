using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AntColonyServer
{

    /// <summary>
    /// Алгоритм муравьиной оптимизации (Ant Colony Optimization)
    /// Разбиение задачи для нескольких клиентов 
    /// </summary>
    public class ServerAnts
    {
        private readonly string _path;  //путь до файла клиента
        private int maxAnts;            // Количество муравьев
        private int numClients;         // Количество клиентов
        private double alpha;           // Влияние феромонов
        private double beta;            // Влияние эвристической информации
        private double RHO;             // Коэффициент испарения феромонов
        private int Q;                  // Константа для обновления феромонов
        private int countSubjects;      // Количество предметов
        private int bestValue;
        private int maxIteration;       // Количество итераций
        private double[] pheromone;     // «привлекательность» каждого элемента или пути для муравьев

        private List<TcpListener> incomingListeners = new List<TcpListener>(); // экземпляры прослушки на вход к клиенту
        private List<TcpListener> outgoingListeners = new List<TcpListener>(); // экземпляры прослушки на выход от клиента
        private List<TcpClient> incomingClients = new List<TcpClient>();       // экземпляры обмена данными на вход клиента
        private List<TcpClient> outgoingClients = new List<TcpClient>();       // экземпляры обмена данными на выход клиенту
        private int inPort;
        private int outPort;
        private IPAddress ipAddress;

        public ServerAnts(IPAddress iPAddress, int inPort, int outPort, int maxAnts = 20, int maxClients = 4, int countSubjects = 1000, int maxIteration = 100)
        {
            this._path = @"C:\dev\csharp\AntDist\Client\bin\Debug\net8.0"; // Путь к Client.dll 
            this.inPort = inPort;
            this.outPort = outPort;
            this.ipAddress = iPAddress;
            this.maxAnts = maxAnts;
            this.numClients = maxClients;
            this.countSubjects = countSubjects;
            this.maxIteration = maxIteration;
            this.alpha = 1.0;
            this.beta = 5.0;
            this.RHO = 0.1;
            this.bestValue = 0;
            this.Q = 100;
            pheromone = Enumerable.Repeat(1.0, countSubjects).ToArray();
        }
        /// <summary>
        /// Загрузка исходного набора данных
        /// </summary>
        /// <param name="countSubjects">кол-во предметов</param>
        /// <returns></returns>
        private (int[] values, int[] weights, int weightLimit) GenerateModelParameters(int countSubjects)
        {
            Random random = new Random(42); // Фиксируем начальное состояние

            int[] values = new int[countSubjects];
            int[] weights = new int[countSubjects];
            for (int i = 0; i < countSubjects; i++)
            {
                values[i] = 100 + random.Next(400); // Значения от 100 до 500
                weights[i] = 10 + random.Next(90);  // Вес ant от 10 до 100
            }

            int weightLimit = 500; // Ограничение для веса

            return (values, weights, weightLimit);
        }

        
        public void StartServer()
        {
            var stopwatch = Stopwatch.StartNew();
            var (values, weights, weightLimit) = GenerateModelParameters(countSubjects);
            var nAnts = CreateAndInitializeSockets(maxAnts, numClients);
           
            stopwatch.Stop();
            TimeSpan clientStartTimer = stopwatch.Elapsed;

            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");
            AcceptClients();

            for (int i = 0; i < outgoingClients.Count; i++)
            {
                var inSocket = incomingClients[i];
                var incomingStream = inSocket.GetStream();

                byte[] buffer = new byte[1024];
                int bytesRead = incomingStream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                if (message == "READY")
                {
                    var outSocket = outgoingClients[i];
                    var outgoingStream = outSocket.GetStream();

                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{alpha};{beta};{nAnts[i]}";
                    byte[] responseBytes = Encoding.UTF8.GetBytes(initData);
                    outgoingStream.Write(responseBytes, 0, responseBytes.Length);
                }
            }

            int bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int iter = 1; iter < maxIteration; iter++)
            {
                
                var (tmpBestValue, tmpBestItems, allValues, allItems) = OneStepAntColony();

                if (tmpBestValue > bestValue)
                {
                    bestValue = tmpBestValue;
                    bestItems = tmpBestItems;
                }

                for (int i = 0; i < pheromone.Length; i++)
                {
                    pheromone[i] = (1.0 - RHO) * pheromone[i];
                }
                for (int k = 0; k < maxAnts; k++)
                {
                    double sumValues = allValues.Sum();
                    foreach (var itemIndex in allItems[k])
                    {
                        pheromone[itemIndex] += Q / sumValues;
                    }
                }
            }

            foreach (var outSocket in outgoingClients)
            {
                SendData(outSocket, "end");
            }
            stopwatch.Stop();
            TimeSpan methodRunTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Состав предметов: {string.Join(",", bestItems)}");
            Console.WriteLine($"--- Общая стоимость : {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");
        }


        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private (int bestValue, List<int> bestItems, List<int> allValues, List<int[]> allItems) OneStepAntColony()
        {
            foreach (var outSocket in outgoingClients)
            {
                SendData(outSocket, string.Join(",", pheromone));
            }

            List<int> bestValues = new List<int>();
            List<List<int>> bestItemsList = new List<List<int>>(numClients);
            List<int> allValues = new List<int>();
            List<int[]> allItems = new List<int[]>();

            for (int i = 0; i < numClients; i++)
            {
                string response = ReceiveData(incomingClients[i]);

                var dataParts = response.Split(';');

                // Получаем и разбираем данные, как и раньше
                int bestValueForClient = int.Parse(dataParts[0]);
                var bestItemsForClient = dataParts[1].Split(' ').Select(int.Parse).ToList();
                var allValuesForClient = dataParts[2].Split(' ').Select(int.Parse).ToList();
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

        public List<int[]> ParseAntSelections(string input)
        {
            string[] antSelections = input.Split(',').Select(s => s.Trim()).ToArray();
            List<int[]> result = antSelections.Select(antSelection => antSelection.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()).ToList();
            return result;
        }


        /// <summary>
        /// Метод для получения TCPClient, подключение клиентов
        /// </summary>
        public void AcceptClients()
        {
            for (int i = 0; i < incomingListeners.Count; i++)
            {
                TcpClient incomingClient = incomingListeners[i].AcceptTcpClient();
                incomingClients.Add(incomingClient);
                //Console.WriteLine($"Клиент {i} подключился к входящему порту {((IPEndPoint)incomingClient.Client.LocalEndPoint).Port}");

                TcpClient outgoingClient = outgoingListeners[i].AcceptTcpClient();
                outgoingClients.Add(outgoingClient);
                //Console.WriteLine($"Клиент {i} подключился к исходящему порту {((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port}");
            }
        }

        private void SendData(TcpClient outSocket, string message)
        {
            var outgoingStream = outSocket.GetStream();
            byte[] responseBytes = Encoding.UTF8.GetBytes(message);
            outgoingStream.Write(responseBytes, 0, responseBytes.Length);
        }

        private string ReceiveData(TcpClient inSocket)
        {
            var incomingStream = inSocket.GetStream();
            byte[] buffer = new byte[65000];
            int bytesRead = incomingStream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0) return "";
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);
        }

        /// <summary>
        /// Создание клиент-серверной архитектуры распределенной системы, запуск клиентов
        /// </summary>
        /// <param name="maxAnts">кол-во муравьев на клиенте</param>
        /// <param name="numSock">кол-во сокетов(листенеров) клиента</param>
        /// <returns></returns>
        private List<int> CreateAndInitializeSockets(int maxAnts, int numSock)
        {
            

            List<int> nAnts = new List<int>();
            int baseNumAnt = maxAnts / numSock;

            // TO DO - отдельной функцией 
            nAnts = Enumerable.Repeat(baseNumAnt, numSock).ToList();

            int addAnt = maxAnts % numSock;
            for (int i = 0; i < addAnt; i++)
            {
                nAnts[i]++;
            }
            Console.WriteLine("Количество муравьев на клиенте: [");
            nAnts.ForEach(x => Console.Write(" " + x));
            Console.WriteLine("\n]");
            Console.WriteLine($"Количество итераций: {maxIteration}");

            for (int i = 0; i < numSock; i++)
            {
               
                TcpListener incomingListener = new TcpListener(this.ipAddress, inPort);
                incomingListener.Start();
                incomingListeners.Add(incomingListener);
                Console.WriteLine($"Входящий слушатель запущен на порту {((IPEndPoint)incomingListener.LocalEndpoint).Port}");

                TcpListener outgoingListener = new TcpListener(this.ipAddress, outPort);
                outgoingListener.Start();
                outgoingListeners.Add(outgoingListener);
                Console.WriteLine($"Исходящий слушатель запущен на порту {((IPEndPoint)outgoingListener.LocalEndpoint).Port}");

                inPort = inPort + 1;
                outPort = outPort + 1;

                StartClientProcess(i);
            }

            return nAnts;
        }


        /// <summary>
        /// Запуск клиентских процессов
        /// </summary>
        /// <param name="clientId">id клиента </param>
        private void StartClientProcess(int clientId)
        {
            Process clientProcess = new Process();
            clientProcess.StartInfo.FileName = "dotnet";
            clientProcess.StartInfo.Arguments = $"Client.dll {clientId}";
            clientProcess.StartInfo.WorkingDirectory = _path;
            clientProcess.Start();
        }
        public void CloseServer()
        {
            // Закрываем все входящие слушатели
            if (incomingListeners != null)
            {
                foreach (var listener in incomingListeners)
                {
                    listener.Stop(); 
                }
                incomingListeners.Clear(); 
            }


            if (outgoingListeners != null)
            {
                foreach (var listener in outgoingListeners)
                {
                    listener.Stop(); 
                }
                outgoingListeners.Clear(); 
            }

            // Закрываем все клиенсткие сокеты
            if (incomingClients != null)
            {
                foreach (var client in incomingClients)
                {
                    client.Close(); 
                }
                incomingClients.Clear(); 
            }

            if (outgoingClients != null)
            {
                foreach (var client in outgoingClients)
                {
                    client.Close(); 
                }
                outgoingClients.Clear(); 
            }
        }


    }

    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                int maxAnts = GetUserInput("Введите кол-во муравьев:");
                int maxClients = GetUserInput("Введите кол-во клиентов:");
                ServerAnts server = new ServerAnts(IPAddress.Loopback, 6000, 7000, maxAnts, maxClients);

                server.StartServer();
                server.CloseServer();
                
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Console.ReadLine();
        }
        static int GetUserInput(string s)
        {
            int result = 0;
            while (true)
            {
                try
                {
                    result = ReadInt(s);
                    break;
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"Некорректный ввод: {ex.Message}");
                }
            }
            return result;

        }

        static int ReadInt(string prompt)
        {
            Console.Write(prompt);
            return int.Parse(Console.ReadLine());
        }
    }

}
