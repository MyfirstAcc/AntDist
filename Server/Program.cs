using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AntColonyServer
{
    public class Server
    {
        private readonly string _path = @"C:\dev\csharp\AntDist\Client\bin\Debug\net8.0"; // Путь к Client.dll 
        public int maxAnts = 20;
        public int numClients = 1;
        private double alpha = 1.0;
        private double beta = 5.0;
        private double RHO = 0.1;
        private int countSubjects = 1000;
        private int port = 2000;
        private int bestValue = 0;
        private int maxIter = 100;
        private double[] pheromone;
        private int Q = 100;
        private List<TcpListener> incomingListeners = new List<TcpListener>();
        private List<TcpListener> outgoingListeners = new List<TcpListener>();
        private List<TcpClient> incomingClients = new List<TcpClient>();
        private List<TcpClient> outgoingClients = new List<TcpClient>();
        private int inPort;
        private int outPort;
        private IPAddress ipAddress;

        public Server(IPAddress iPAddress, int inPort, int outPort)
        {
           this.inPort = inPort;
           this.outPort = outPort;
           this.ipAddress = iPAddress;
           pheromone = Enumerable.Repeat(1.0, (int)countSubjects).ToArray();
        }

        private (int[] values, int[] weights, int weightLimit) GenerateModelParameters(int countSubjects)
        {
            //int[] weights = { 391, 444, 250, 330, 246, 400, 150, 266, 268, 293, 471, 388, 364, 493, 202, 161, 410, 270, 384, 486 };
            //int[] values = { 55, 52, 59, 24, 52, 46, 45, 34, 34, 59, 59, 28, 57, 21, 47, 66, 64, 42, 22, 23 };
            //int weightLimit = 500;

            Random random = new Random(42); // Фиксируем начальное состояние

            //// Генерация значений
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


        public async Task StartServer()
        {
            var stopwatch = Stopwatch.StartNew();
            var (values, weights, weightLimit) = GenerateModelParameters(countSubjects);
            var nAnts = CreateAndInitializeSockets(maxAnts, numClients);
            stopwatch.Stop();
            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиента: {clientStartTimer.TotalSeconds} с.");
            await AcceptClients();
           


            for (int i = 0; i < outgoingClients.Count; i++)
            {
                var inSocket = incomingClients[i];
                var incomingStream = inSocket.GetStream();
                
                byte[] buffer = new byte[1024];
                int bytesRead = await incomingStream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                if (message == "READY")
                {
                    var outSocket = outgoingClients[i];

                    var outgoingStream = outSocket.GetStream();


                    //Создание строки initData
                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{alpha};{beta};{nAnts[i]}";

                    byte[] responseBytes = Encoding.UTF8.GetBytes(initData);
                    await outgoingStream.WriteAsync(responseBytes, 0, responseBytes.Length);

                }
            }

            int bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int iter = 1; iter < maxIter; iter++)
            {
                var (tmpBestValue, tmpBestItems, allValues, allItems) = await OneStepAntColony();

                if (tmpBestValue > bestValue)
                {
                    bestValue = tmpBestValue;
                    bestItems = tmpBestItems;
                }

                // Обновление феромонов
                for (int i = 0; i < pheromone.Length; i++)
                {
                    pheromone[i] = (1.0 - RHO) * pheromone[i];
                }
                for (int k = 0; k < maxAnts; k++)
                {
                    double sumValues = allValues.Sum(); // сумма элементов подмассива
                    foreach (var itemIndex in allItems[k])
                    {

                        pheromone[itemIndex] += Q / sumValues;
                    }
                    
                }
                //Console.WriteLine($"Итерация {iter}: Стоимость {bestValue}");

            }
            foreach (var outSocket in outgoingClients)
            {
                 SendData(outSocket,"end");
            }
            stopwatch.Stop();
            TimeSpan methodRunTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Состав предметов: {string.Join(",", bestItems)}");
            Console.WriteLine($"--- Общая стоимость : {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer+methodRunTimer).TotalSeconds} с.");

        }


        private async Task<(int bestValue, List<int> bestItems, List<int> allValues, List<int[]> allItems)> OneStepAntColony()
        {
            foreach (var outSocket in outgoingClients)
            {
                await SendData(outSocket, string.Join(",", pheromone));
            }

            // Инициализация переменных для хранения результатов
            List<int> bestValues = new List<int>();
            List<List<int>> bestItemsList = new List<List<int>>(numClients);
            List<int> allValues = new List<int>();
            List<int[]> allItems = new List<int[]>();

            // Получение данных от каждого сокета
            for (int i = 0; i < numClients; i++)
            {
                string response = await ReceiveData(incomingClients[i]);

                var dataParts = response.Split(';');
                allItems = ParseAntSelections(dataParts[3]);
                allValues = dataParts[1].Split(' ').Select(int.Parse).ToList();
                bestValues.Add(int.Parse(dataParts[0]));
                bestItemsList.Add(dataParts[1].Split(' ').Select(int.Parse).ToList());
                
            }

            // Выбор лучшего значения
            int maxIndex = bestValues.IndexOf(bestValues.Max());
            int bestValue = bestValues[maxIndex];
            List<int> bestItems = bestItemsList[maxIndex];

            return (bestValue, bestItems, allValues, allItems);
        }

        public List<int[]> ParseAntSelections(string input)
        {
            // Разделяем строку на подстроки по запятым 
            // "1 2 3, 4 5, 6" -> ["1 2 3", "4 5", "6"]
            string[] antSelections = input.Split(',')
                                        .Select(s => s.Trim())
                                        .ToArray();

            // Преобразуем каждую подстроку в массив чисел 
            // ["1 2 3", "4 5", "6"] -> [[1,2,3], [4,5], [6]]
            List<int[]> result = antSelections
                .Select(antSelection =>
                    antSelection.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                               .Select(int.Parse)
                               .ToArray())
                .ToList();

            return result;
        }

        public async Task AcceptClients()
        {
            for (int i = 0; i < incomingListeners.Count; i++)
            {
                // Принимаем входящее соединение
                TcpClient incomingClient = await incomingListeners[i].AcceptTcpClientAsync();
                incomingClients.Add(incomingClient);
                Console.WriteLine($"Клиент {i} подключился к входящему порту {((IPEndPoint)incomingClient.Client.LocalEndPoint).Port}");

                // Принимаем исходящее соединение
                TcpClient outgoingClient = await outgoingListeners[i].AcceptTcpClientAsync();
                outgoingClients.Add(outgoingClient);
                Console.WriteLine($"Клиент {i} подключился к исходящему порту {((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port}");
            }
        }

        private async Task SendData(TcpClient outSocket, string message)
        {
            var outgoingStream = outSocket.GetStream();

            byte[] responseBytes = Encoding.UTF8.GetBytes(message);
            await outgoingStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        }


        private async Task<string> ReceiveData(TcpClient inSocket)
        {
            var incomingStream = inSocket.GetStream();

            byte[] buffer = new byte[65000];
            int bytesRead = await incomingStream.ReadAsync(buffer, 0, buffer.Length);
            if (bytesRead == 0) return "";
            return Encoding.UTF8.GetString(buffer, 0, bytesRead);

        }

        private List<int> CreateAndInitializeSockets(int maxAnts, int numSock)
        {
            List<int> nAnts = new List<int>();

            Console.WriteLine("% Вычисление количества муравьев для каждого сокета");

            // Расчёт базового количества муравьев для каждого сокета
            int baseNumAnt = maxAnts / numSock;
            nAnts = Enumerable.Repeat(baseNumAnt, numSock).ToList();

            // Распределение оставшихся муравьёв
            int addAnt = maxAnts % numSock;
            for (int i = 0; i < addAnt; i++)
            {
                nAnts[i]++;
            }

            Console.WriteLine("--- Количество муравьев на клиенте: ");
            nAnts.ForEach(x => Console.WriteLine(x));

            Console.WriteLine("% Создание и настройка сокетов");

            for (int i = 0; i < numSock; i++)
            {
                inPort = inPort + i;
                outPort = outPort + i;

                // Настройка прослушивания входящих сообщений
                TcpListener incomingListener = new TcpListener(this.ipAddress, inPort);
                incomingListener.Start();
                incomingListeners.Add(incomingListener);
                Console.WriteLine($"Входящий слушатель запущен на порту {((IPEndPoint)incomingListener.LocalEndpoint).Port}");

                // Настройка прослушивания исходящих сообщений
                TcpListener outgoingListener = new TcpListener(this.ipAddress, outPort);
                outgoingListener.Start();
                outgoingListeners.Add(outgoingListener);
                Console.WriteLine($"Исходящий слушатель запущен на порту {((IPEndPoint)outgoingListener.LocalEndpoint).Port}");

                StartClientProcess(i);

            }
          
            return nAnts;

        }
        private void StartClientProcess(int clientId)
        {
            Process clientProcess = new Process();
            clientProcess.StartInfo.FileName = "dotnet";
            clientProcess.StartInfo.Arguments = $"Client.dll {clientId}";
            clientProcess.StartInfo.WorkingDirectory = _path;
            //clientProcess.StartInfo.UseShellExecute = false; // Если требуется, укажите false
            //clientProcess.StartInfo.RedirectStandardOutput = true; // Если хотите видеть вывод
            //clientProcess.StartInfo.RedirectStandardError = true; // Для ошибок
            clientProcess.Start();
        }
    }


        class Program
    {
        static async Task Main(string[] args)
        {
            Server server = new Server(IPAddress.Loopback,6000, 7000);
            //int result;
            //while (true)
            //{
            //    try
            //    {
            //        result = ReadInt32("Введите кол-во муравьев: ");
            //        break;
            //    }
            //    catch (FormatException ex)
            //    {
            //        Console.WriteLine($"Некорректный ввод: {ex.Message}");
            //    }
            //}
            //server.maxAnts = result;

            //while (true)
            //{
            //    try
            //    {
            //        result = ReadInt32("Введите кол-во клиентов: ");
            //        break;
            //    }
            //    catch (FormatException ex)
            //    {
            //        Console.WriteLine($"Некорректный ввод: {ex.Message}");
            //    }
            //}
            //server.numClients = result;

            await server.StartServer();
            
            Console.ReadLine();
        }

        static int ReadInt32(string prompt)
        {
            Console.Write(prompt);
            return int.Parse(Console.ReadLine());
        }
    }

}
