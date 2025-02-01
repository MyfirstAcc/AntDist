document.getElementById('connection-form').addEventListener('submit', async (event) => {
    event.preventDefault();

    const serverIP = document.getElementById('server-ip').value;
    const serverPort = document.getElementById('server-port').value;
    const serverUri = `ws://${serverIP}:${serverPort}/`;

    const messagesElement = document.getElementById('messages');
    const logMessage = (message) => {
        messagesElement.textContent += message + '\n';
    };

    try {
        const webSocket = new WebSocket(serverUri);
        let bestValue = 0;
        let bestItems = [];


        webSocket.onopen = () => {
            logMessage(`Connected to server: ${serverUri}`);
            webSocket.send("READY");
        };

        webSocket.onmessage = async (event) => {
            const message = event.data;
           
            if (message === "end") {
                webSocket.close();
                logMessage(`Received: ${message}`);
                logMessage("Connection closed by server.");
                logMessage(bestValue);
                return;
            }

            if (!client.initData) {
                client.splitInitData(message);
                logMessage(`Received: ${message}`);
                webSocket.send("READY");
                return;
            }

            client.pheromone = message.split(',').map(Number);
           
            const allValues = [];
            const allItems = [];

            for (let i = 0; i < client.nAnts; i++) {
                const { chosenItems, currentValue } = client.antSolution();
                allValues.push(currentValue);
                allItems.push(chosenItems);

                if (currentValue > bestValue) {
                    bestValue = currentValue;
                    bestItems = chosenItems;
                }
            }

            const allItemsStr = allItems.map((items) => items.join(' ')).join(',');
            const toSend = `${bestValue};${bestItems.join(' ')};${allValues.join(' ')};${allItemsStr}`;
            webSocket.send(toSend);           
        };

        webSocket.onclose = () => {
            logMessage("WebSocket connection closed.");
        };

        webSocket.onerror = (error) => {
            logMessage(`Error: ${error.message}`);
        };

        const client = {
            weights: [],
            values: [],
            weightLimit: 0,
            alpha: 0,
            beta: 0,
            nAnts: 0,
            pheromone: [],

            splitInitData(initData) {
                const dataParts = initData.split(';');
                this.weights = dataParts[0].split(',').map(Number);
                this.values = dataParts[1].split(',').map(Number);
                this.weightLimit = parseInt(dataParts[2], 10);
                this.alpha = parseFloat(dataParts[3]);
                this.beta = parseFloat(dataParts[4]);
                this.nAnts = parseInt(dataParts[5], 10);
                this.initData = true;
            },

            antSolution() {
                let currentWeight = 0;
                let currentValue = 0;
                const chosenItems = [];
                let availableItems = Array.from({ length: this.weights.length }, (_, i) => i);

                while (availableItems.length > 0) {
                    const probabilities = availableItems.map(
                        (i) =>
                            Math.pow(this.pheromone[i], this.alpha) *
                            Math.pow(this.values[i] / this.weights[i], this.beta)
                    );

                    const sumProb = probabilities.reduce((a, b) => a + b, 0);
                    const normalizedProb = probabilities.map((p) => p / sumProb);

                    const cumulativeProb = normalizedProb.reduce((acc, p, i) => {
                        acc.push((acc[i - 1] || 0) + p);
                        return acc;
                    }, []);

                    const r = Math.random();
                    const selectedItemIndex = cumulativeProb.findIndex((p) => r <= p);
                    const selectedItem = availableItems[selectedItemIndex];

                    if (currentWeight + this.weights[selectedItem] <= this.weightLimit) {
                        chosenItems.push(selectedItem);
                        currentWeight += this.weights[selectedItem];
                        currentValue += this.values[selectedItem];
                        availableItems.splice(selectedItemIndex, 1);
                    } else {
                        break;
                    }
                }

                return { chosenItems, currentValue };
            },
        };
    } catch (error) {
        console.error(`Failed to connect: ${error.message}`);
    }
});
