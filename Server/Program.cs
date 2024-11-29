using Humanizer;
using Microsoft.Extensions.Configuration;
using System;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security;
using System.Security.Policy;
using System.Text;
using System.Web.Services.Description;
using System.Collections.Concurrent;
using Server;

namespace AntColonyServer
{
    public class ServerConfig
    {
        public string[] NameClients { get; set; }
        public int MaxAnts { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int InPort { get; set; }
        public int maxIteration { get; set; }
        public double Alpha { get; set; }
        public double Beta { get; set; }
        public int Q { get; set; }
        public double RHO { get; set; }
        public int CountSubjects { get; set; }
        public string PathToEXE { get; set; }
        public string NameFile { get; set; }

        public bool LocalTest { get; set; }
        public bool UploadFile { get; set; }

        public ServerConfig()
        {
            NameClients ??= new string[0];
            MaxAnts = 20;
            Username = "";
            Password = "";
            InPort = 7080;
            maxIteration = 100;
            Alpha = 1.0;
            Beta = 5.0;
            Q = 100;
            RHO = 0.1;
            CountSubjects = 1000;
            PathToEXE = "C:\\temp";
            NameFile = "Client.exe";
            LocalTest = true;
            UploadFile = false;
        }
    }

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

        private HttpListener listener;
        private ConcurrentDictionary<int, WebSocket> clients;

        private int inPort;
        private IPAddress ipAddress;
        private ServerConfig serverConfig;
        private List<Pipeline> pipeline;
        private List<Runspace> runSpace;
        

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
            pipeline = new List<Pipeline>(this.numClients);
            runSpace = new List<Runspace>(this.numClients);
            listener = new HttpListener();
            clients = new ConcurrentDictionary<int, WebSocket>();

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
            InitializeWebSockets();

            var stopwatch = Stopwatch.StartNew();
            ChooseAndRunClients();
            await AcceptClients();
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

        private void InitializeWebSockets()
        {
            var erorrFlag = true;      
            while (erorrFlag)
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add($"http://{ipAddress}:{inPort}/");
                    listener.Start();
                    erorrFlag= false;
                }
                catch (SocketException ex)
                {                   
                    inPort++;
                    erorrFlag = true;
                    continue;                   
                }
            }

        }
        
        private void ChooseAndRunClients()
        {
            if (serverConfig.UploadFile == true)
            {
                for (int i = 0; i < numClients; i++)
                {

                    DeployRemoteApp(serverConfig.NameClients[i], Path.Combine(serverConfig.PathToEXE.ToString(),
                    serverConfig.NameFile), Path.Combine(Directory.GetCurrentDirectory(),
                        serverConfig.NameFile), i, serverConfig.Username, serverConfig.Password);

                }
            }

            for (int i = 0; i < numClients; i++)
            {
                if (serverConfig.LocalTest == true)
                {
                    StartClientProcess(i);
                }
                else
                {
                    ExecuteRemoteApp(serverConfig.NameClients[i], Path.Combine(serverConfig.PathToEXE,
                        serverConfig.NameFile), i, serverConfig.Username, serverConfig.Password);
                }
            }
        }

        /// <summary>
        /// Метод для получения TCPClient, подключение клиентов
        /// </summary>
        private async Task AcceptClients()
        {           
            for (int i = 0; i < numClients; i++)
            {
                HttpListenerContext context = await listener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
                    clients[i] = wsContext.WebSocket;
                    var listeners = this.listener.Prefixes;
                    int port = 0;
                    foreach (var lestiner in listeners)
                    {
                        Uri uri = new Uri(lestiner);
                        port = uri.Port;

                    }
                    Console.WriteLine($"Клиент {i} подключился к порту {port}");

                }
                else
                {
                    context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    context.Response.Close();
                }

            }
            Console.WriteLine($"{new string('-', 32)}");
        }

        private void DeployRemoteApp(string remoteComputer, string remotePath, string localFilePath, int i, string username, string password)
        {

            ///
            string copyFileCommand = $@"
                    $session = New-PSSession -ComputerName '{remoteComputer}' -Credential (New-Object System.Management.Automation.PSCredential('{username}', (ConvertTo-SecureString '{password}' -AsPlainText -Force)));
                    Copy-Item -Path '{localFilePath}' -Destination '{remotePath}' -ToSession $session;
              ";


            // Выполнение команды на локальной PowerShell-сессии - да, это очень странно, но работает xD
            var psLocal = PowerShell.Create();

            psLocal.AddScript(copyFileCommand);
            var resultsps = psLocal.Invoke();

            if (psLocal.Streams.Error.Count > 0)
            {
                Console.WriteLine("Ошибка при копировании файла:");
                foreach (var error in psLocal.Streams.Error)
                {
                    Console.WriteLine(error.ToString());
                }
                return;
            }
            else
            {
                Console.WriteLine($"Копирование файла на {serverConfig.NameClients[i]} завершено успешно.");
            }
        }

        private void ExecuteRemoteApp(string remoteComputer, string remotePath, int idClient, string username, string password)
        {
            SecureString securePassword = new SecureString();
            foreach (char c in password)
                securePassword.AppendChar(c);
            securePassword.MakeReadOnly();            

            // Формируем команду
            string executeCommand = $"Start-Process -FilePath {remotePath} -ArgumentList \"{ipAddress} {getPort()}\"";
            string script = $"{executeCommand}";

            // Создаем объект PSCredential с именем пользователя и паролем
            var credential = new PSCredential(username, securePassword);

            // Настраиваем подключение с использованием WSManConnectionInfo
            var connectionInfo = new WSManConnectionInfo(new Uri($"http://{remoteComputer}:5985/wsman"), "http://schemas.microsoft.com/powershell/Microsoft.PowerShell", credential)
            {
                AuthenticationMechanism = AuthenticationMechanism.Negotiate
            };

            // Открываем удалённое подключение и выполняем команды
            runSpace.Add(RunspaceFactory.CreateRunspace(connectionInfo));

            runSpace[idClient].Open();
            pipeline.Add(runSpace[idClient].CreatePipeline());


            pipeline[idClient].Commands.AddScript(script);
            var results = pipeline[idClient].Invoke();

            foreach (var item in results)
            {
                Console.WriteLine(item);
            }

        }

        private int getPort()
        {
            var listeners = this.listener.Prefixes;
            int port = 0;
            foreach (var lestiner in listeners)
            {
                Uri uri = new Uri(lestiner);
                port = uri.Port;

            }
            return port;
        }

        private void StartClientProcess(int clientId)
        {
            Process clientProcess = new Process();
            clientProcess.StartInfo.FileName = serverConfig.NameFile;
           
            clientProcess.StartInfo.Arguments = $"{ipAddress} {getPort()}";
            clientProcess.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            clientProcess.Start();
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

       
        private async Task SendData(int clientIndex, string message)
        {
            if (clients[clientIndex].State == WebSocketState.Open)
            {
                byte[] responseBuffer = Encoding.UTF8.GetBytes(message);
                await clients[clientIndex].SendAsync(new ArraySegment<byte>(responseBuffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public async Task<string> ReceiveData(int clientIndex, int countBuffer)
        {
            byte[] buffer = new byte[countBuffer];

            if (clients[clientIndex].State == WebSocketState.Open)
            {
                var result = await clients[clientIndex].ReceiveAsync(new ArraySegment<byte>(buffer),
                    CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    return Encoding.UTF8.GetString(buffer, 0, result.Count);
                }

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

        public async void CloseServer()
        {
            // Закрытие всех pipeline
            if (pipeline != null)
            {
                foreach (var pipe in pipeline)
                {
                    if (pipe != null)
                    {
                        pipe.Dispose();
                    }
                }
                pipeline.Clear();
                pipeline = null;
            }

            // Закрытие всех runspace
            if (runSpace != null)
            {
                foreach (var space in runSpace)
                {
                    if (space != null)
                    {
                        space.Close();
                        space.Dispose();
                    }
                }
                runSpace.Clear();
                runSpace = null;
            }

            if (listener != null)
            {
                listener.Stop();
                listener.Close();
            }

            //if (clients != null)
            //{
            //    foreach (var socket in clients)
            //    {
            //        if (socket.State == WebSocketState.Open)
            //        {
            //            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            //            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            //        }
            //    }
            //}
        }

    }

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
                var storage = new SQLiteDatabase(dbFilePath);
                int testRunId = storage.AddTestRun(typeTest, DateTime.Now);

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
