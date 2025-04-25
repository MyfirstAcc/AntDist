document.addEventListener('DOMContentLoaded', () => {
    let ws;
    setupWebSocket();

    document.getElementById('startServer').addEventListener('click', () => sendWebSocketMessage({ type: 'start' }));
    document.getElementById('stopServer').addEventListener('click', () => sendWebSocketMessage({ type: 'stop' }));
    document.getElementById('saveConfig').addEventListener('click', saveConfig);
    document.getElementById('clearLog').addEventListener('click', () => {
        document.getElementById('logContainer').innerHTML = '';
    });

    function setupWebSocket() {
        ws = new WebSocket(`ws://${window.location.host}/ws`);
        const logContainer = document.getElementById('logContainer');

        ws.onopen = () => {
            sendWebSocketMessage({ type: 'get_config' });
            sendWebSocketMessage({ type: 'get_results' });
        };

        ws.onmessage = (event) => {
            const data = JSON.parse(event.data);
            switch (data.type) {
                case 'log':
                    const logEntry = document.createElement('div');
                    logEntry.textContent = data.message;
                    logContainer.appendChild(logEntry);
                    logContainer.scrollTop = logContainer.scrollHeight;
                    break;
                case 'config':
                    document.getElementById('alpha').value = data.data.Alpha;
                    document.getElementById('beta').value = data.data.Beta;
                    document.getElementById('q').value = data.data.Q;
                    document.getElementById('rho').value = data.data.RHO;
                    document.getElementById('maxAnts').value = data.data.MaxAnts;
                    document.getElementById('maxIteration').value = data.data.MaxIteration;
                    document.getElementById('numClients').value = data.data.NumClients;
                    document.getElementById('countSubjects').value = data.data.CountSubjects;
                    break;
                case 'results':
                    const tableBody = document.getElementById('resultsTable');
                    tableBody.innerHTML = '';
                    data.data.forEach(result => {
                        const row = document.createElement('tr');
                        row.innerHTML = `
                            <td class="border p-2">${result.TestRunId}</td>
                            <td class="border p-2 overflow-hidden truncate w-2">${result.TestType}</td>
                            <td class="border p-2">${new Date(result.Data).toLocaleString()}</td>
                            <td class="border p-2 line-clamp-2 text-gray-700">${result.BestItems}</td>
                            <td class="border p-2">${result.BestValue}</td>
                            <td class="border p-2">${result.MethodRunTime}</td>
                            <td class="border p-2">${result.TotalRunTime}</td>
                        `;
                        tableBody.appendChild(row);
                    });
                    break;
                case 'status':
                    document.getElementById('serverStatus').textContent = data.message;
                    break;
                case 'error':
                    document.getElementById('serverStatus').textContent = `Ошибка: ${data.message}`;
                    break;
            }
        };

        ws.onclose = () => {
            const logEntry = document.createElement('div');
            logEntry.textContent = `${new Date().toLocaleString()}: WebSocket соединение закрыто`;
            logContainer.appendChild(logEntry);
            logContainer.scrollTop = logContainer.scrollHeight;
            setTimeout(setupWebSocket, 5000); // Автопереподключение через 5 секунд
        };

        ws.onerror = (error) => {
            const logEntry = document.createElement('div');
            logEntry.textContent = `${new Date().toLocaleString()}: Ошибка WebSocket: ${error}`;
            logContainer.appendChild(logEntry);
            logContainer.scrollTop = logContainer.scrollHeight;
        };
    }

    function sendWebSocketMessage(message) {
        if (ws && ws.readyState === WebSocket.OPEN) {
            ws.send(JSON.stringify(message));
        } else {
            document.getElementById('serverStatus').textContent = 'Ошибка: WebSocket не подключён';
        }
    }

    function saveConfig() {
        const config = {
            Alpha: parseFloat(document.getElementById('alpha').value),
            Beta: parseFloat(document.getElementById('beta').value),
            Q: parseFloat(document.getElementById('q').value),
            RHO: parseFloat(document.getElementById('rho').value),
            MaxAnts: parseInt(document.getElementById('maxAnts').value),
            MaxIteration: parseInt(document.getElementById('maxIteration').value),
            NumClients: parseInt(document.getElementById('numClients').value),
            CountSubjects: parseInt(document.getElementById('countSubjects').value)
        };
        sendWebSocketMessage({ type: 'set_config', data: config });
    }
});