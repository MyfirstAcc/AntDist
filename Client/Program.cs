using Microsoft.VisualBasic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

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
        incomingStream.Close();
        outgoingStream.Close();
        incomingClient.Close();
        outgoingClient.Close();
    }

    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Нет аргументов командной строки.");
            return;
        }
        int nid = int.Parse(args[0]);
        AntClient client = new AntClient();
        client.ConnectToServer(IPAddress.Loopback, 7000 + nid, 6000 + nid);

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
}
