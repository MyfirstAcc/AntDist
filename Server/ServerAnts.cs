using System.Diagnostics;
using System.Management.Automation.Runspaces;
using System.Management.Automation;
using System.Net.Sockets;
using System.Net;
using System.Security;
using System.Text;
using Humanizer;

namespace AntColonyServer
{
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

        private ProtocolType protocolType; // Тип протокола: TCP или UDP
        private List<Socket> incomingSockets = new List<Socket>();       // экземпляры обмена данными на вход клиента
        private List<Socket> outgoingSockets = new List<Socket>();       // экземпляры обмена данными на выход клиенту
        private List<Socket> incomingClients = new List<Socket>();
        private List<Socket> outgoingClients = new List<Socket>();
        private List<IPEndPoint> clientEndPoints = new List<IPEndPoint>(); // Список удаленных конечных точек для UDP

        private int inPort;
        private int outPort;
        private IPAddress ipAddress;

        private PowerShell psLocal;
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
            outPort = serverConfig.OutPort;
            pheromone = Enumerable.Repeat(1.0, countSubjects).ToArray();
            pipeline = new List<Pipeline>(this.numClients);
            runSpace = new List<Runspace>(this.numClients);
            protocolType = serverConfig.ProtocolType.ToLower() == "tcp" ? ProtocolType.Tcp : ProtocolType.Udp;


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

        // Метод для суммирования цифр числа
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


        public (List<int> bestItems, int bestValue, TimeSpan methodRunTimer, TimeSpan totalTime) StartServer()
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

            ChooseAndRunClients();

            AcceptClients();
            stopwatch.Stop();
            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");

            for (
                int i = 0; i < numClients; i++)
            {
                var message = ReceiveData(i, 1024);
                if (message == "READY")
                {
                    string initData = $"{string.Join(",", weights)};{string.Join(",", values)};{weightLimit};{alpha};{beta};{nAnts[i]}";
                    SendData(i, initData);
                }
            }

            bestValue = 0;
            List<int> bestItems = new List<int>();
            stopwatch.Reset();
            stopwatch.Start();
            for (int i = 0; i < numClients; i++)
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

            for (int j = 0; j < numClients; j++)
            {
                SendData(j, "end");
            }
            stopwatch.Stop();
            TimeSpan methodRunTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Состав предметов: {string.Join(",", bestItems)}");
            Console.WriteLine($"--- Общая стоимость: {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");
            return (bestItems, bestValue, methodRunTimer, (clientStartTimer + methodRunTimer));
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
                    inPort++;
                    outPort++;
                    continue;
                }
            }

        }

        private void InitializeUdpSockets()
        {
            int successfulClients = 0; // Счетчик для успешных соединений
            while (successfulClients < numClients)
            {
                try
                {
                    Socket incomingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);      
                    incomingSocket.Bind(new IPEndPoint(ipAddress, inPort));
                    incomingSockets.Add(incomingSocket);                   

                    IPEndPoint clientEndPoint = new IPEndPoint(ipAddress, outPort);
                    clientEndPoints.Add(clientEndPoint);

                    outPort++;
                    inPort++;
                    successfulClients++;
                }
                catch (SocketException ex)
                {
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
            if (protocolType == ProtocolType.Tcp)
            {
                Socket inc = incomingSockets[clientId];
                Socket outc = outgoingSockets[clientId];

                clientProcess.StartInfo.Arguments = $"{ipAddress} {((IPEndPoint)inc.LocalEndPoint).Port} {((IPEndPoint)outc.LocalEndPoint).Port} {protocolType.ToString()}";
            }
            else
            {
                clientProcess.StartInfo.Arguments = $"{ipAddress} {((IPEndPoint)incomingSockets[clientId].LocalEndPoint).Port} {clientEndPoints[clientId].Port} {protocolType.ToString()}";
            }
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
                    Console.WriteLine($"UDP сокет готов на порту {inPort}...");
                    
                }
            }
            Console.WriteLine($"{new string('-', 32)}");
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
                var outgoingSocket = incomingSockets[clientIndex];
                EndPoint clientEndPoint = clientEndPoints[clientIndex];
                outgoingSocket.SendTo(data, clientEndPoint);
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
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                int bytesRead = incomingSockets[clientIndex].ReceiveFrom(buffer, ref remoteEP);
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                return Encoding.UTF8.GetString(buffer, 0, bytesRead);
            }
            throw new Exception("Не удается принять сообщение");
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
}
