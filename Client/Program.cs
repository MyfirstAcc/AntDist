using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

class AntClient
{
    private Socket incomingSocket;
    private Socket outgoingSocket;
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
        incomingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        outgoingSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // Подключение к входящему и исходящему порту на сервере
        incomingSocket.Connect(serverAddress, incomingPort);
        outgoingSocket.Connect(serverAddress, outgoingPort);

        Console.WriteLine($"Подключен к серверу: {incomingPort} (вход) и {outgoingPort} (выход)");
    }

    public void SendMessage(string message)
    {
        // Отправка сообщения через указанный сокет
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        outgoingSocket.Send(messageBytes);
    }

    public string ReceiveMessage(int countBuffer)
    {
        // Чтение данных из указанного сокета
        byte[] buffer = new byte[65000];
        int bytesRead = incomingSocket.Receive(buffer);     
        return Encoding.UTF8.GetString(buffer, 0, bytesRead);
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
        if (incomingSocket != null)
        {
            incomingSocket.Close();
            incomingSocket = null;
        }
        if (outgoingSocket != null)
        {
            outgoingSocket.Close();
            outgoingSocket = null;
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

            var str = client.ReceiveMessage(2048);
            client.SplitInitData(str);

            client.SendMessage("READY");

            while (true)
            {
                string inData = client.ReceiveMessage(65000);
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
        }
        finally
        {
            client.Close();
        }
    }
}
