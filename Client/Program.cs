using Microsoft.VisualBasic;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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


    public async Task ConnectToServer(IPAddress serverAddress, int incomingPort, int outgoingPort)
    {
        incomingClient = new TcpClient();
        outgoingClient = new TcpClient();

        // Подключение к входящему и исходящему порту на сервере
        await incomingClient.ConnectAsync(serverAddress, incomingPort);
        await outgoingClient.ConnectAsync(serverAddress, outgoingPort);

        incomingStream = incomingClient.GetStream();
        outgoingStream = outgoingClient.GetStream();

        Console.WriteLine($"Подключен к серверу на обоих портах:({((IPEndPoint)incomingClient.Client.LocalEndPoint).Port})" +
            $"--->({incomingPort}) ({((IPEndPoint)outgoingClient.Client.LocalEndPoint).Port})--->({outgoingPort})");
    }

    public async Task SendMessage(string message)
    {
        // Отправка сообщения на входящий порт сервера
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await outgoingStream.WriteAsync(messageBytes, 0, messageBytes.Length);

        
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

    public async Task<string> ReceivedMessage()
    {
        // Чтение ответа из потока исходящих сообщений
        byte[] buffer = new byte[65000];
        int bytesRead = await incomingStream.ReadAsync(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, bytesRead);
    }


    private (List<int> chosenItems, int currentValue) AntSolution()
    {
        double currentWeight = 0;    // Текущий вес рюкзака
        int currentValue = 0;     // Текущая стоимость предметов в рюкзаке
        var chosenItems = new List<int>();  // Список выбранных предметов
        var availableItems = Enumerable.Range(0, weights.Length).ToList();  // Индексы доступных предметов

        Random random = new Random();

        // Процесс выбора предметов до тех пор, пока есть доступные предметы
        while (availableItems.Count > 0)
        {
            var probabilities = availableItems.Select(i =>
                Math.Pow(pheromone[i], alpha) * Math.Pow(values[i] / weights[i], beta)).ToArray();

            // Нормализация вероятностей для получения распределения
            double sumProb = probabilities.Sum();
            var normalizedProb = probabilities.Select(p => p / sumProb).ToArray();

            // Вычисление накопительных вероятностей для случайного выбора
            var cumulativeProb = new double[normalizedProb.Length];
            cumulativeProb[0] = normalizedProb[0];
            for (int i = 1; i < normalizedProb.Length; i++)
            {
                cumulativeProb[i] = cumulativeProb[i - 1] + normalizedProb[i];
            }

            // Поиск случайного числа и выбор на основе накопительных вероятностей
            double r = random.NextDouble();
            int selectedItemIndex = Array.FindIndex(cumulativeProb, p => r <= p);
            int selectedItem = availableItems[selectedItemIndex];

            //Console.WriteLine($"Length of chosen_items: {chosenItems.Count}");

            // Проверка возможности добавления выбранного предмета в рюкзак
            if (currentWeight + weights[selectedItem] <= weightLimit)
            {
                // Добавление предмета в список выбранных
                chosenItems.Add(selectedItem);
                currentWeight += weights[selectedItem];
                currentValue += values[selectedItem];
                // Удаление выбранного предмета из списка доступных
                availableItems.RemoveAt(selectedItemIndex);
            }
            else
            {
                // Завершение цикла, если больше нет подходящих предметов
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

    static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("No arguments provided.");
            return;
        }
        int nid = int.Parse(args[0]);
        //Console.ReadLine();
        AntClient client = new AntClient();
        await client.ConnectToServer(IPAddress.Loopback, 7000 + nid, 6000 + nid);

        await client.SendMessage("READY");

        var str = await client.ReceivedMessage();
        client.SplitInitData(str);

        
        while (true)
        {
            string inData = await client.ReceivedMessage();
            if (inData == "end")
            {
                break;
            }
            else
            {
                client.pheromone = Array.ConvertAll(inData.Split(','), double.Parse);
                int bestValue = 0;
                int[] bestItems = [];
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

                string msg = "";
                //Console.WriteLine(allItems.Length);

                string allItemsStr = string.Join(",", Array.ConvertAll(allItems, items => string.Join(" ", items)));
                string toSend = $"{bestValue};{string.Join(" ", bestItems)};{string.Join(" ", allValues)};{allItemsStr}";
                //Console.WriteLine($"{ bestValue} {bestItems.Length} {allValues.Length} {allItems.Length}");
                await client.SendMessage(toSend);
            }
           
        }

        client.Close();
    }
}
