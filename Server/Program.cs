using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;

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
            this._path = @"./"; // Путь к Client.dll 
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
            Console.WriteLine($"{new string('-', 32)}");

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
            Console.WriteLine($"--- Общая стоимость: {bestValue}");
            Console.WriteLine($"--- Время выполнения алгоритма: {methodRunTimer.TotalSeconds} с.");
            Console.WriteLine($"--- Общее время выполнения: {(clientStartTimer + methodRunTimer).TotalSeconds} с.");
        }




        //public void DeployAndExecuteRemoteApp(string remoteComputer, string localFilePath, string remotePath, int nClient, string username, string password)
        //{
        //    try
        //    {
        //        // Step 1: Копирование файла на удалённый компьютер
        //        CopyFileToRemote(remoteComputer, localFilePath, remotePath, username, password);

        //        // Step 2: Запуск приложения на удалённом компьютере
        //        ExecuteRemoteApp(remoteComputer, remotePath, nClient, username, password);

        //        // Step 3: Удаление файла после выполнения
        //       // DeleteRemoteFile(remoteComputer, remotePath, username, password);
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Ошибка при развертывании и выполнении удалённого приложения: {ex.Message}");
        //    }
        //}

        //private void CopyFileToRemote(string remoteComputer, string localFilePath, string remotePath, string username, string password)
        //{
        //    string command = $@"\\{remoteComputer} -u {username} -p {password} cmd /c copy '{ localFilePath}' '{remotePath}'";
        //    ExecutePsExecCommand(command);
        //}

        //private void ExecuteRemoteApp(string remoteComputer, string remotePath, int nClient, string username, string password)
        //{
        //    string command = $@"\\{remoteComputer} -u {username} -p {password} -d '{ remotePath}\' {nClient}";
        //    ExecutePsExecCommand(command);
        //}

        //private void DeleteRemoteFile(string remoteComputer, string remotePath, string username, string password)
        //{
        //    string command = $@"{remoteComputer} -u {username} -p {password} cmd /c del '{ remotePath}'";
        //    ExecutePsExecCommand(command);
        //}

        //private void ExecutePsExecCommand(string command)
        //{
        //    ProcessStartInfo startInfo = new ProcessStartInfo
        //    {
        //        FileName = psexecPath,
        //        Arguments = command,
        //        RedirectStandardOutput = true,
        //        UseShellExecute = false,
        //        CreateNoWindow = true,
        //    };

        //    using (Process process = new Process { StartInfo = startInfo })
        //    {
        //        process.Start();
        //        string output = process.StandardOutput.ReadToEnd();
        //        process.WaitForExit();
        //        Console.WriteLine(output);
        //    }
        //}




        public void DeployAndExecuteRemoteApp(string remoteComputer, string localFilePath, string remotePath, int nClient, string username)
        {
            // Step 1: Copy file to remote machine
            string copyCommand = $"Copy-Item -Path {localFilePath} -Destination {remotePath}";

            // Step 2: Run the exe file on the remote machine
            string executeCommand = $"Start-Process -FilePath {remotePath} -ArgumentList \"{nClient} {ipAddress}\"";

            // Step 3: Delete the exe file after execution
            string deleteCommand = $"Remove-Item -Path '{remotePath}' -Force";

            // Combine the commands
            string script = $"{executeCommand}";

            // Создаем объект SecureString для пароля
            SecureString securePassword = new SecureString();
            foreach (char c in "admin")
                securePassword.AppendChar(c);
            securePassword.MakeReadOnly();
            

            // Создаем объект PSCredential с именем пользователя и паролем
            var credential = new PSCredential(username, securePassword);

            // Настраиваем подключение с использованием WSManConnectionInfo
            var connectionInfo = new WSManConnectionInfo(new Uri($"http://{remoteComputer}:5985/wsman"), "http://schemas.microsoft.com/powershell/Microsoft.PowerShell", credential)
            {
                AuthenticationMechanism = AuthenticationMechanism.Negotiate
            };

            // Открываем удалённое подключение и выполняем команды
            using (var runspace = RunspaceFactory.CreateRunspace(connectionInfo))
            {

                runspace.Open();
                var pipeline = runspace.CreatePipeline();
                {

                    pipeline.Commands.AddScript(script);
                    var results = pipeline.Invoke();

                    foreach (var item in results)
                    {
                        Console.WriteLine(item);
                    }
                }
            }
            
           
        }

        public void DeployAndExecuteRemoteApp(string remoteComputer, string localFilePath, string remotePath, int nClient, decimal d)
        {
            string passwordS = "admin";
            SecureString passwordSecure = new SecureString();
            foreach (var item in passwordS)
            {
                passwordSecure.AppendChar(item);
            }
            passwordSecure.MakeReadOnly();

            //// Step 1: Copy file to remote machine
            //string copyCommand = $"Copy-Item -Path '{localFilePath}' -Destination '{remotePath}'";

            //// Step 2: Run the exe file on the remote machine
            //string executeCommand = $"Start-Process -FilePath '{remotePath}' -ArgumentList '{nClient} {ipAddress}'";

            //// Step 3: Delete the exe file after execution
            ////string deleteCommand = $"Remove-Item -Path '{remotePath}' -Force";

            //// Combine the commands
            //string script = $"{copyCommand}; {executeCommand}; ";



            var credential = new PSCredential("Администратор", passwordSecure);


            var connectionInfo = new WSManConnectionInfo(new Uri($"http://{remoteComputer}:5985/wsman"), "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
                credential);
            // Создаем и открываем удалённую сессию
            using (var runspace = RunspaceFactory.CreateRunspace(connectionInfo))
            {
                runspace.Open();

                using (var ps = PowerShell.Create())
                {
                    ps.Runspace = runspace;

                    // Step 1: Создание сессии
                    ps.AddCommand("New-PSSession")
                      .AddParameter("ComputerName", remoteComputer)
                      .AddParameter("Credential", credential);
                    var sessions = ps.Invoke();
                    //if (sessions.Count == 0)
                    //{
                    //    Console.WriteLine("Failed to create remote session.");
                    //    return;
                    //}
                    var session = sessions[0];

                    // Step 2: Копирование файла в сессию
                    ps.Commands.Clear();
                    ps.AddCommand("Copy-Item")
                      .AddParameter("Path", localFilePath)
                      .AddParameter("Destination", remotePath)
                      .AddParameter("ToSession", session);
                    ps.Invoke();

                    // Step 3: Выполнение exe файла
                    string executeCommand = $"Start-Process -FilePath '{remotePath}' -ArgumentList '{nClient}'";
                    ps.Commands.Clear();
                    ps.AddScript(executeCommand);
                    ps.Invoke();

                    // Step 4: Завершение сессии
                    ps.Commands.Clear();
                    ps.AddCommand("Remove-PSSession")
                      .AddParameter("Session", session);
                    ps.Invoke();
                }

            }
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

                DeployAndExecuteRemoteApp("DESKTOP-19B6D0D", "C:/test.txt", "C:/temp/net8.0/Client.exe", i, "Администратор");
                //StartClientProcess(i);
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
            clientProcess.StartInfo.FileName = "Client.exe"; // Укажите имя вашего .exe файла
            clientProcess.StartInfo.Arguments = $"{clientId} {ipAddress}"; // Аргументы, которые передаются .exe
            clientProcess.StartInfo.WorkingDirectory = _path; // Папка, где находится .exe
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
            Console.WriteLine($"{new string('-',32)}");
            Console.WriteLine("Алгоритм муравьиной оптимизации");
            Console.WriteLine();
            try
            {
                int maxAnts = GetUserInput("Введите кол-во муравьев: ");
                int maxClients = GetUserInput("Введите кол-во клиентов: ");
                int maxIter = GetUserInput("Введите кол-во итераций: ");
                ServerAnts server = new ServerAnts(IPAddress.Parse("192.168.1.30"), 6000, 7000, maxAnts, maxClients, 1000, maxIter);
                Console.WriteLine($"{new string('-', 32)}");
                try
                {
                    server.StartServer();
                    server.CloseServer();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.ToString());
                    server.CloseServer();
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
            finally
            {
                
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
                    int res = Math.Sign(result = ReadInt(s));
                    if (res == 1)
                    {
                        break;
                    }
                    else
                    {
                        throw new Exception("Значение должно быть > 0");
                    }
                }
                catch (Exception ex)
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
