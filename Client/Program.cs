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

    public void ConnectToServer(IPAddress serverAddress, int Port)
    {
        incomingClient = new TcpClient();
        incomingClient.Connect(serverAddress, Port);
        
    }

    public void ReconnectToServer(IPAddress serverAddress, int incomingPort, int outgoingPort)
    {
        incomingClient = new TcpClient();
        outgoingClient = new TcpClient();

        // Подключение к входящему и исходящему порту на сервере
        incomingClient.Connect(serverAddress, incomingPort);
        outgoingClient.Connect(serverAddress, outgoingPort);

        incomingStream = incomingClient.GetStream();
        outgoingStream = outgoingClient.GetStream();

        Console.WriteLine($"Подключен к серверу на обоих портах:({((IPEndPoint)incomingClient.Client.LocalEndPoint).Port})" +
        $"--->({incomingPort}) ({((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port})--->({outgoingPort})");
    }

    public void SendMessage(string message)
    {

        // Отправка сообщения на входящий порт сервера
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        outgoingStream.Write(messageBytes, 0, messageBytes.Length);
    }


    public void SendMessage(TcpClient outSock, string message)
    {
        var outStream = outSock.GetStream();
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        outStream.Write(messageBytes, 0, messageBytes.Length);
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

    public string ReceivedMessage(TcpClient outSock)
    {
        var inStream = outSock.GetStream();
        // Чтение ответа из потока исходящих сообщений
        byte[] buffer = new byte[65000];
        int bytesRead = inStream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, bytesRead);
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
        double currentWeight = 0;
        int currentValue = 0;
        var chosenItems = new List<int>();
        var availableItems = Enumerable.Range(0, weights.Length).ToList();

        Random random = new Random();

        while (availableItems.Count > 0)
        {
            var probabilities = availableItems.Select(i =>
                Math.Pow(pheromone[i], alpha) * Math.Pow(values[i] / weights[i], beta)).ToArray();

            double sumProb = probabilities.Sum();
            var normalizedProb = probabilities.Select(p => p / sumProb).ToArray();

            var cumulativeProb = new double[normalizedProb.Length];
            cumulativeProb[0] = normalizedProb[0];
            for (int i = 1; i < normalizedProb.Length; i++)
            {
                cumulativeProb[i] = cumulativeProb[i - 1] + normalizedProb[i];
            }

            double r = random.NextDouble();
            int selectedItemIndex = Array.FindIndex(cumulativeProb, p => r <= p);
            int selectedItem = availableItems[selectedItemIndex];

            if (currentWeight + weights[selectedItem] <= weightLimit)
            {
                chosenItems.Add(selectedItem);
                currentWeight += weights[selectedItem];
                currentValue += values[selectedItem];
                availableItems.RemoveAt(selectedItemIndex);
            }
            else
            {
                break;
            }
        }

        return (chosenItems, currentValue);
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
        Console.ReadLine();
        AntClient client = new AntClient();
        try
        {
            if (args.Length == 0)
            {
                throw new Exception("Нет аргументов командной строки.");
            }
            int nid = int.Parse(args[0]);
            string ip = args[1];
            
            IPAddress ipa = IPAddress.Parse(ip);
            client.ConnectToServer(ipa, nid);
            client.SendMessage(client.incomingClient, "Hello!");

            string data = client.ReceivedMessage(client.incomingClient); 
            int[] arr = Array.ConvertAll(data.Split(" "), int.Parse);
            client.Close();
            Console.WriteLine($"Reconnect to {arr[1]} {arr[0]}");

            client.ReconnectToServer(ipa, arr[1], arr[0]);
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
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            Thread.Sleep(1000);
        }
        finally
        {
            client.Close();
        }
    }
}
