using Humanizer;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Text;
using System.Web.Services.Description;

namespace AntColonyServer
{


    // Более-менее рабочая версия 
    //код - рука лицо... xD
    public class ServerConfig
    {
        public string[] NameClients { get; set; }
        public int MaxAnts { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public int InPort { get; set; }
        public int OutPort { get; set; }
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

        public string LogFilePath { get; set; }

        public string ProtocolType { get; set; }

        public ServerConfig()
        {
            NameClients ??= new string[0];
            MaxAnts = 20;
            Username = "";
            Password = "";
            InPort = 7080;
            OutPort = 9090;
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
            LogFilePath = "";
            ProtocolType = "tcp";
        }
    }

    public class MultiTextWriter : TextWriter
    {
        private readonly TextWriter[] writers;

        public MultiTextWriter(params TextWriter[] writers)
        {
            this.writers = writers;
        }

        public override Encoding Encoding => writers[0].Encoding;

        public override void Write(char value)
        {
            foreach (var writer in writers)
            {
                writer.Write(value);
            }
        }

        public override void WriteLine(string value)
        {
            foreach (var writer in writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void Flush()
        {
            foreach (var writer in writers)
            {
                writer.Flush();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var writer in writers)
                {
                    writer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }


    /// <summary>
    /// Алгоритм муравьиной оптимизации (Ant Colony Optimization)
    /// Разбиение задачи для нескольких клиентов 
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
        private double[] pheromone;     // «привлекательность» каждого элемента или пути для муравьев
        private List<IPAddress> ipClients;

        private readonly ProtocolType protocolType; // Тип протокола: TCP или UDP
        private List<Socket> incomingSockets = new List<Socket>();       // экземпляры обмена данными на вход клиента
        private List<Socket> outgoingSockets = new List<Socket>();       // экземпляры обмена данными на выход клиенту
        private List<Socket> incomingClients = new List<Socket>();
        private List<Socket> outgoingClients = new List<Socket>();


        private int inPort;
        private int outPort;
        private IPAddress ipAddress;     

        private PowerShell psLocal;
        private ServerConfig serverConfig;
        private List<Pipeline> pipeline;
        private List<Runspace> runSpace;
        MultiTextWriter multiTextWriter;

        public ServerAnts(IPAddress iPAddress, ServerConfig serverConfig, MultiTextWriter multiTextWriter)
        {
            this.ipAddress = iPAddress;
            this.serverConfig = serverConfig;
            this.numClients = 1;//serverConfig.NameClients.Length == 0 ? 4: serverConfig.NameClients.Length;
            this.alpha = serverConfig.Alpha;
            this.beta = serverConfig.Beta;
            this.RHO = serverConfig.RHO;
            this.Q = serverConfig.Q;
            this.countSubjects = serverConfig.CountSubjects;
            this.bestValue = 0;
            this.maxIteration = serverConfig.maxIteration;
            this.inPort = serverConfig.InPort;
            this.outPort = serverConfig.OutPort;
            this.pheromone = Enumerable.Repeat(1.0, countSubjects).ToArray();
            this.pipeline = new List<Pipeline>(this.numClients);
            this.runSpace = new List<Runspace>(this.numClients);
            this.multiTextWriter = multiTextWriter;
            this.protocolType = serverConfig.ProtocolType.ToLower() == "tcp" ? ProtocolType.Tcp : ProtocolType.Udp;

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

            var (values, weights, weightLimit) = GenerateModelParameters(countSubjects);
            var nAnts = NumberOfAntsPerClient(serverConfig.MaxAnts, numClients);

            if (protocolType == ProtocolType.Tcp)
            {
                Console.WriteLine("Запуск сервера в режиме TCP...");
                InitializeTcpSockets();
            }
            else if (protocolType == ProtocolType.Udp)
            {
                Console.WriteLine("Запуск сервера в режиме UDP...");
                InitializeUdpSockets();
            }
            else
            {
                throw new NotSupportedException("Указанный протокол не поддерживается.");
            }


            var stopwatch = Stopwatch.StartNew();

//            ChooseAndRunClients();

            AcceptClients();
            stopwatch.Stop();
            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");

            for (int i = 0; i < outgoingSockets.Count; i++)
            {
                var message = ReceiveData(i, 1024);
                if (message == "READY")
                {
                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{alpha};{beta};{nAnts[i]}";
                    SendData(i, initData);
                }
            }

            int bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int i = 0; i < outgoingSockets.Count; i++)
            {
                var message = ReceiveData(i, 1024);
                if (message != "READY")
                {
                    break;
                }
            }

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
                for (int k = 0; k < serverConfig.MaxAnts; k++)
                {
                    double sumValues = allValues.Sum();
                    foreach (var itemIndex in allItems[k])
                    {
                        pheromone[itemIndex] += Q / sumValues;
                    }
                }
            }

            for (int j = 0; j <numClients; j++ )
            {
                SendData(j, "end");
            }
            stopwatch.Stop();
            TimeSpan methodRunTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Состав предметов: {string.Join(",", bestItems)}");
            Console.WriteLine($"--- Общая стоимость: {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");

        }

        private void InitializeTcpSockets()
        {
            int successfulClients = 0; // Счетчик для успешных соединений
            while (successfulClients < numClients)
            {
                try
                {
                    Socket incomingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    incomingSocket.Bind(new IPEndPoint(ipAddress, inPort));
                    incomingSocket.Listen(numClients);
                    incomingSockets.Add(incomingSocket);
                    Console.WriteLine(ipAddress);

                    Socket outgoingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    outgoingSocket.Bind(new IPEndPoint(ipAddress, outPort));
                    outgoingSocket.Listen(numClients);
                    outgoingSockets.Add(outgoingSocket);

                    outPort++;
                    inPort++;
                    successfulClients++;
                }
                catch (SocketException ex)
                {
                    //Console.WriteLine($"Ошибка при запуске слушателя на порту {inPort}: {ex.Message}");
                    // Пробуем с новыми значениями портов, не увеличивая счётчик
                    inPort++;
                    outPort++;
                    continue;
                }
            }

        }
        
        private void InitializeUdpSockets()
        {
            int successfulClients = 0; // Счетчик для успешных соединений
            while (successfulClients <= numClients)
            {
                try
                {
                    Socket incomingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    incomingSocket.Bind(new IPEndPoint(ipAddress, inPort));
                    incomingSockets.Add(incomingSocket);

                    Socket outgoingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    outgoingSocket.Bind(new IPEndPoint(ipAddress, outPort));
                    outgoingSockets.Add(outgoingSocket);

                    outPort++;
                    inPort++;
                    successfulClients++;
                }
                catch (SocketException ex)
                {
                    //Console.WriteLine($"Ошибка при запуске слушателя на порту {inPort}: {ex.Message}");
                    // Пробуем с новыми значениями портов, не увеличивая счётчик
                    inPort++;
                    outPort++;
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
                    serverConfig.NameFile),Path.Combine(Directory.GetCurrentDirectory(),
                        serverConfig.NameFile), i, serverConfig.Username, serverConfig.Password);

                }
            }

            for (int i = 0; i <numClients; i++)
            {              
                if (serverConfig.LocalTest == true)
                {
                    StartClientProcess(i);
                }
                else
                {                   
                    ExecuteRemoteApp(serverConfig.NameClients[i], Path.Combine(serverConfig.PathToEXE, 
                        serverConfig.NameFile),i,serverConfig.Username,serverConfig.Password);
                }
            }
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

            Socket inc = incomingSockets[idClient];
            Socket outc = outgoingSockets[idClient];

            int inPort = ((IPEndPoint)inc.LocalEndPoint).Port;
            int outPort = ((IPEndPoint)outc.LocalEndPoint).Port;

            // Формируем команду
            string executeCommand = $"Start-Process -FilePath {remotePath} -ArgumentList \"{ipAddress} {inPort} {outPort}\"";
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

        private void StartClientProcess(int clientId)
        {
            Process clientProcess = new Process();
            clientProcess.StartInfo.FileName = serverConfig.NameFile;
            Socket inc = this.incomingSockets[clientId];
            Socket outc = this.outgoingSockets[clientId];
           
            clientProcess.StartInfo.Arguments = $"{ipAddress} {((IPEndPoint)inc.LocalEndPoint).Port} {((IPEndPoint)inc.LocalEndPoint).Port}";
            clientProcess.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            clientProcess.Start();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private (int bestValue, List<int> bestItems, List<int> allValues, List<int[]> allItems) OneStepAntColony()
        {

            for (int i = 0; i < numClients; i++)
            {

                SendData(i, string.Join(",", pheromone));
            }

            List<int> bestValues = new List<int>();
            List<List<int>> bestItemsList = new List<List<int>>(numClients);
            List<int> allValues = new List<int>();
            List<int[]> allItems = new List<int[]>();

            for (int i = 0; i < this.numClients; i++)
            {
                string response = ReceiveData(i, 65000);
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

        /// <summary>
        /// Метод для получения TCPClient, подключение клиентов
        /// </summary>
        private void AcceptClients()
        {
            if (multiTextWriter is not null)
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),Encoding.GetEncoding(866)) { AutoFlush = true });
            }
            for (int i = 0; i < numClients; i++)
            {
                if (protocolType == ProtocolType.Tcp)
                {
                    Socket clientSocketIn = incomingSockets[i].Accept(); // Только для TCP
                    incomingClients.Add(clientSocketIn);
                    Console.WriteLine($"Клиент {i} подключился к  порту {((IPEndPoint)clientSocketIn.LocalEndPoint)}");
                    Socket clientSocketOut = outgoingSockets[i].Accept();
                    outgoingClients.Add(clientSocketOut);
                    Console.WriteLine($"Клиент {i} подключился к порту {((IPEndPoint)clientSocketOut.LocalEndPoint)}");
                    Console.WriteLine($"{ipAddress} {((IPEndPoint)clientSocketIn.LocalEndPoint).Port} {((IPEndPoint)clientSocketOut.LocalEndPoint).Port}");
                }
                else
                {
                    Console.WriteLine($"UDP сокет готов на порту {inPort + i}...");
                    // UDP не требует Accept, данные принимаются сразу
                }
            }
            Console.WriteLine($"{new string('-', 32)}");

            if (multiTextWriter is not null)
            {
                Console.SetOut(multiTextWriter);
            }
        }

        private void SendData(int clientIndex, string message)
        { 
            byte[] data = Encoding.UTF8.GetBytes(message);

            if (protocolType == ProtocolType.Tcp)
            {
                var clientSocket = outgoingClients[clientIndex];
                clientSocket.Send(data);
            }
            else if (protocolType == ProtocolType.Udp)
            {
                var clientSocket = outgoingClients[clientIndex];
                var endpoint = new IPEndPoint(ipAddress, outPort + clientIndex);
                clientSocket.SendTo(data, endpoint);
            }
        }

        public string ReceiveData(int clientIndex, int countBuffer)
        {
            byte[] buffer = new byte[countBuffer];
            if (protocolType == ProtocolType.Tcp)
            {
                var clientSocket = incomingClients[clientIndex];
                int bytesRead = clientSocket.Receive(buffer);
                if (bytesRead == -1) return string.Empty;
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            else if (protocolType == ProtocolType.Udp)
            {
                var clientSocket = incomingClients[clientIndex];
                EndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                int bytesRead = clientSocket.ReceiveFrom(buffer, ref remoteEndpoint);
                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            return string.Empty;
        }

        private List<int> NumberOfAntsPerClient(int maxAnts,int numSock)
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


            foreach (var socket in incomingSockets.Concat(outgoingSockets))
            {
                if (socket.Connected)
                    socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
            incomingSockets.Clear();
            outgoingSockets.Clear();

            // Закрываем все клиенсткие сокеты
            if (incomingSockets != null)
            {
                foreach (var client in incomingSockets)
                {
                  client.Close();
                }
                incomingSockets.Clear();
            }

            if (outgoingSockets != null)
            {
                foreach (var client in outgoingSockets)
                {
                    client.Close();
                }
                outgoingSockets.Clear();
            }
        }

    }

    class Program
    {
        static void Main(string[] args)
        {

            Console.WriteLine($"{new string('-', 32)}");
            Console.WriteLine("Алгоритм муравьиной оптимизации");
            Console.WriteLine($"{new string('-', 32)}");
            // Регистрация поддержки дополнительных кодировок
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try
            {
                var configuration = new ConfigurationBuilder()
                              .SetBasePath(Directory.GetCurrentDirectory())
                              .AddJsonFile("config.json", optional: false, reloadOnChange: true)
                              .Build();

                var config = configuration.Get<ServerConfig>() ?? new ServerConfig();
                var nameClientsValue = configuration.GetSection("nameClients").Value;
                StreamWriter? StreamLogFile;
                MultiTextWriter? multiTextWriter;
                StreamLogFile = config.LogFilePath != "" ? new StreamWriter(config.LogFilePath, append: true) { AutoFlush = true } : null;
                multiTextWriter = StreamLogFile is not null ? new MultiTextWriter(Console.Out, StreamLogFile) : null;
                if (multiTextWriter is not null)
                {
                    Console.SetOut(multiTextWriter);
                }

                if (int.TryParse(nameClientsValue, out int clientCount))
                {
                    
                    config.NameClients = new string[clientCount];
                    for (int i = 0; i < clientCount; i++)
                    {
                        config.NameClients[i] = $"{i+1}"; // инициализируем пустыми строками
                    }
                }
                else
                {
                    // Если это массив, получаем его напрямую
                    config.NameClients = configuration.GetSection("nameClients").Get<string[]>();
                }



                ServerAnts server = new ServerAnts(IPAddress.Parse(GetLocalIPAddress()), config, multiTextWriter);              
                ShowConfig(config);

                try
                {
                    server.StartServer();
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

                if (multiTextWriter is not null)
                {
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }         
        }
 
        static void ShowConfig(ServerConfig serverConfig)
        {
            Console.WriteLine("{0,30}","-----Конфигурация(config.json)-----");
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
