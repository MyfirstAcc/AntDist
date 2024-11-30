using System.Diagnostics;
using System.Management.Automation.Runspaces;
using System.Management.Automation;
using System.Net.Sockets;
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
        private readonly int numClients;         // Количество клиентов
        private readonly double alpha;           // Влияние феромонов
        private readonly double beta;            // Влияние эвристической информации
        private readonly double RHO;             // Коэффициент испарения феромонов
        private readonly int Q;                  // Константа для обновления феромонов
        private readonly int countSubjects;      // Количество предметов
        private int bestValue;
        private readonly int maxIteration;       // Количество итераций
        private double[] pheromone;             // «привлекательность» каждого элемента или пути для муравьев

        private List<TcpListener> incomingListeners = new List<TcpListener>(); // экземпляры прослушки на вход к клиенту
        private List<TcpListener> outgoingListeners = new List<TcpListener>(); // экземпляры прослушки на выход от клиента
        private List<TcpClient> incomingClients = new List<TcpClient>();       // экземпляры обмена данными на вход клиента
        private List<TcpClient> outgoingClients = new List<TcpClient>();       // экземпляры обмена данными на выход клиенту
        private int inPort;
        private int outPort;
        private IPAddress ipAddress;

        private PowerShell psLocal;
        private ServerConfig serverConfig;
        private List<Pipeline> pipeline;
        private List<Runspace> runspace;

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
            runspace = new List<Runspace>(this.numClients);

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
            var stopwatch = Stopwatch.StartNew();
            var (values, weights, weightLimit) = GenerateModelParameters(countSubjects);
            var nAnts = NumberOfAntsPerClient(serverConfig.MaxAnts, numClients);
            CreateAndInitializeSockets(numClients);
            ChooseAndRunClients();

            AcceptClients();
            stopwatch.Stop();
            TimeSpan clientStartTimer = stopwatch.Elapsed;
            Console.WriteLine($"--- Время запуска клиентских сокетов : {clientStartTimer.TotalSeconds} с.");

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
                for (int k = 0; k < serverConfig.MaxAnts; k++)
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
            Console.WriteLine($"--- Общая стоимость: {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма:{methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");

            return (bestItems, bestValue, methodRunTimer, (clientStartTimer + methodRunTimer));

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

            TcpListener inc = incomingListeners[idClient];
            TcpListener outc = outgoingListeners[idClient];

            string executeCommand = $"Start-Process -FilePath {remotePath} -ArgumentList \" {ipAddress} {((IPEndPoint)inc.LocalEndpoint).Port} {((IPEndPoint)outc.LocalEndpoint).Port}\"";
            string script = $"{executeCommand}";

            // Создаем объект PSCredential с именем пользователя и паролем
            var credential = new PSCredential(username, securePassword);

            // Настраиваем подключение с использованием WSManConnectionInfo
            var connectionInfo = new WSManConnectionInfo(new Uri($"http://{remoteComputer}:5985/wsman"), "http://schemas.microsoft.com/powershell/Microsoft.PowerShell", credential)
            {
                AuthenticationMechanism = AuthenticationMechanism.Negotiate
            };

            // Открываем удалённое подключение и выполняем команды
            runspace.Add(RunspaceFactory.CreateRunspace(connectionInfo));

            runspace[idClient].Open();
            pipeline.Add(runspace[idClient].CreatePipeline());


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
            TcpListener inc = this.incomingListeners[clientId];
            TcpListener outc = this.outgoingListeners[clientId];
            clientProcess.StartInfo.Arguments = $"{ipAddress} {((IPEndPoint)inc.LocalEndpoint).Port} {((IPEndPoint)outc.LocalEndpoint).Port}";
            clientProcess.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            clientProcess.Start();
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

            for (int i = 0; i < this.numClients; i++)
            {
                string response = ReceiveData(incomingClients[i]);
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

                TcpClient incomingClient = incomingListeners[i].AcceptTcpClient();
                incomingClients.Add(incomingClient);

                Console.WriteLine($"Клиент {i} подключился к входящему порту {((IPEndPoint)incomingClient.Client.LocalEndPoint).Port}");
                TcpClient outgoingClient = outgoingListeners[i].AcceptTcpClient();
                outgoingClients.Add(outgoingClient);
                Console.WriteLine($"Клиент {i} подключился к исходящему порту {((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port}");
            }
            Console.WriteLine($"{new string('-', 32)}");
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
        /// <param name="numSock">кол-во сокетов(листенеров) клиента</param>
        /// <returns></returns>
        private void CreateAndInitializeSockets(int numSock)
        {
            int successfulClients = 0; // Счетчик для успешных соединений

            while (successfulClients <= numClients)
            {
                try
                {
                    TcpListener incomingListener = new TcpListener(ipAddress, inPort);
                    incomingListener.Start();
                    incomingListeners.Add(incomingListener);

                    TcpListener outgoingListener = new TcpListener(ipAddress, outPort);
                    outgoingListener.Start();
                    outgoingListeners.Add(outgoingListener);

                    //Console.WriteLine($"Исходящий слушатель запущен на порту {((IPEndPoint)outgoingListener.LocalEndpoint).Port}");

                    // Увеличиваем порты только после успешного создания обоих сокетов
                    inPort++;
                    outPort++;
                    successfulClients++; // Увеличиваем счётчик успешных соединений
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
            if (runspace != null)
            {
                foreach (var space in runspace)
                {
                    if (space != null)
                    {
                        space.Close();
                        space.Dispose();
                    }
                }
                runspace.Clear();
                runspace = null;
            }


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
}
