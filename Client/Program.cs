using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

class AntClient
{
    private ClientWebSocket webSocket;
    private string initData;
    private int[] weights;
    private int[] values;
    private int weightLimit;
    private double alpha;
    private double beta;
    private int nAnts;
    public double[] pheromone;

    public async Task ConnectToServerAsync(Uri serverUri)
    {
        webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(serverUri, CancellationToken.None);

        //Console.WriteLine($"Подключен к серверу: {serverUri}");
    }

    public async Task SendMessageAsync(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        await webSocket.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public async Task<string> ReceiveMessageAsync()
    {
        byte[] buffer = new byte[65000];
        var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Text)
        {
            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        return string.Empty;
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

    public async Task CloseAsync()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            //await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing connection", CancellationToken.None);
        }
        webSocket?.Dispose();
        webSocket = null;
    }

    static async Task Main(string[] args)
    { 
        AntClient client = new AntClient();

        try
        {
            if (args.Length == 0)
            {
                throw new Exception("Нет аргументов командной строки.");
            }
            string serverIP = args[0];
            string serverPort = args[1];

            Uri serverAddress = new Uri($"ws://{serverIP}:{serverPort}/");
            await client.ConnectToServerAsync(serverAddress);

            await client.SendMessageAsync("READY");

            var str = await client.ReceiveMessageAsync();
            client.SplitInitData(str);

            await client.SendMessageAsync("READY");

            while (true)
            {
                string inData = await client.ReceiveMessageAsync();
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
                    await client.SendMessageAsync(toSend);
                }
            }

            await client.CloseAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        finally
        {
            await client.CloseAsync();
        }
    }
}
