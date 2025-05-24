using System.Net;
using System.Net.Sockets;
using System.Text;

class AntClient
{
    private TcpClient incomingClient;
    private TcpClient outgoingClient;
    private NetworkStream incomingStream;
    private NetworkStream outgoingStream;
    private string initData;
    private int[] weights;
    private int[] values;
    private int weightLimit;
    private double alpha;
    private double beta;
    private int nAnts;
    public double[] pheromone;

    public void ConnectToServer(IPAddress serverAddress, int incomingPort, int outgoingPort)
    {
        incomingClient = new TcpClient();
        outgoingClient = new TcpClient();

        // Подключение к входящему и исходящему порту на сервере
        incomingClient.Connect(serverAddress, incomingPort);
        outgoingClient.Connect(serverAddress, outgoingPort);

        incomingStream = incomingClient.GetStream();
        outgoingStream = outgoingClient.GetStream();

        //Console.WriteLine($"Подключен к серверу на обоих портах:({((IPEndPoint)incomingClient.Client.LocalEndPoint).Port})" +
        //$"--->({incomingPort}) ({((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port})--->({outgoingPort})");
    }

    public void SendMessage(string message)
    {
        // Отправка сообщения на входящий порт сервера
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        outgoingStream.Write(messageBytes, 0, messageBytes.Length);
    }

    public void SplitInitData(string initData)
    {
        var dataParts = initData.Split(';');
        weights = Array.ConvertAll(dataParts[0].Split(','), int.Parse);
        values = Array.ConvertAll(dataParts[1].Split(','), int.Parse);
        weightLimit = int.Parse(dataParts[2]);
        alpha = double.Parse(dataParts[3]);
        beta = double.Parse(dataParts[4]);
        nAnts = int.Parse(dataParts[5]);
    }

    public string ReceivedMessage()
    {
        // Чтение ответа из потока исходящих сообщений
        byte[] buffer = new byte[65000];
        int bytesRead = incomingStream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }

    private (List<int> chosenItems, int currentValue) AntSolution()
    {
        double currentWeight = 0; // Текущий суммарный вес (ресурсы, wj)
        int currentValue = 0; // Текущая суммарная ценность (cj, например, углеродное поглощение)
        var chosenItems = new List<int>(); // Список выбранных кварталов (Sk)
        var availableItems = Enumerable.Range(0, weights.Length).ToList(); // Множество доступных кварталов (allowed)

        Random random = new Random(); // Генератор случайных чисел для вероятностного выбора

        while (availableItems.Count > 0) // Пока есть доступные кварталы
        {
            // Вычисление вероятностей выбора для каждого доступного квартала (формула 1: pj^k(t))
            // Числитель: τj(t)^α * ηj^β, где ηj = values[i] / weights[i] (ценность/вес)
            var probabilities = availableItems.Select(i =>
                Math.Pow(pheromone[i], alpha) * Math.Pow(values[i] / weights[i], beta)).ToArray();

            // Нормализация вероятностей: деление на сумму (знаменатель формулы 1)
            double sumProb = probabilities.Sum();
            var normalizedProb = probabilities.Select(p => p / sumProb).ToArray();

            // Вычисление кумулятивных вероятностей для выбора квартала
            double[] cumulativeProb = new double[normalizedProb.Length];
            cumulativeProb[0] = normalizedProb[0];
            for (int i = 1; i < normalizedProb.Length; i++)
            {
                cumulativeProb[i] = cumulativeProb[i - 1] + normalizedProb[i];
            }

            // Вероятностный выбор квартала (на основе формулы 1)
            double r = random.NextDouble();
            int selectedItemIndex = Array.FindIndex(cumulativeProb, p => r <= p);
            int selectedItem = availableItems[selectedItemIndex];

            // Проверка ограничения на ресурсы: ∑w_j ≤ W (из математической модели)
            if (currentWeight + weights[selectedItem] <= weightLimit)
            {
                chosenItems.Add(selectedItem); // Добавление квартала в решение S_k
                currentWeight += weights[selectedItem]; // Обновление текущего веса
                currentValue += values[selectedItem]; // Обновление текущей ценности
                availableItems.RemoveAt(selectedItemIndex); // Удаление выбранного квартала из allowed
            }
            else
            {
                break; // Прерывание, если превышен лимит ресурсов
            }
        }

        return (chosenItems, currentValue); // Возврат решения Sk и его ценности c(Sk)
    }

    public void Close()
    {
        if (incomingStream != null)
        {
            incomingStream.Close();
            incomingStream = null;
        }
        if (outgoingStream != null)
        {
            outgoingStream.Close();
            outgoingStream = null;
        }
        if (incomingClient != null)
        { 
            incomingClient.Close();
            incomingClient = null;
        }
        if (outgoingClient != null)
        {
            outgoingClient.Close();
            outgoingClient = null;

        }
    }

    static void Main(string[] args)
    {
        AntClient client = new AntClient();
        try
        {
            if (args.Length == 0)
            {
                throw new Exception("Нет аргументов командной строки.");
            }
            string IP = args[0];
            int inPort = int.Parse(args[1]);
            int outPort = int.Parse(args[2]);

            IPAddress IPParse = IPAddress.Parse(IP);
            client.ConnectToServer(IPParse, outPort, inPort);
           
            client.SendMessage("READY");

            var str = client.ReceivedMessage();
            client.SplitInitData(str);

            while (true)
            {
                string inData = client.ReceivedMessage();
                if (inData == "end")
                {
                    break;
                }
                else
                {
                    client.pheromone = Array.ConvertAll(inData.Split(','), double.Parse);
                    int bestValue = 0;
                    int[] bestItems = new int[0];
                    int[] allValues = new int[client.nAnts];
                    int[][] allItems = new int[client.nAnts][];

                    for (int i = 0; i < client.nAnts; i++)
                    {
                        (List<int> chosenItems, int currentValue) = client.AntSolution();
                        allValues[i] = currentValue;
                        allItems[i] = chosenItems.ToArray();

                        if (currentValue > bestValue)
                        {
                            bestValue = currentValue;
                            bestItems = chosenItems.ToArray();
                        }
                    }

                    string allItemsStr = string.Join(",", Array.ConvertAll(allItems, items => string.Join(" ", items)));
                    string toSend = $"{bestValue};{string.Join(" ", bestItems)};{string.Join(" ", allValues)};{allItemsStr}";
                    client.SendMessage(toSend);

                }
            }
            client.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Thread.Sleep(1000);
        }
        finally
        {
            //client.Close();
        }
    }
}
