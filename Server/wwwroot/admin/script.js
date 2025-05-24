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
                case 'chart_data':
                    renderCharts(data.data);
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

    function renderCharts(data) {
        // Преобразование данных для графиков
        const groupedData = {};
        data.forEach(item => {
            item.NumClients.forEach(clientCount => {
                if (!groupedData[clientCount]) {
                    groupedData[clientCount] = { bestValue: [], methodRunTime: [], startTimeClient: [] };
                }
                groupedData[clientCount].bestValue.push(item.BestValue);
                groupedData[clientCount].methodRunTime.push(item.MethodRunTime);
                groupedData[clientCount].startTimeClient.push(item.StartTimeClient);
            });
        });

        // Вычисление средних значений
        const chartData = Object.keys(groupedData).map(clientCount => ({
            numClients: parseInt(clientCount),
            avgBestValue: groupedData[clientCount].bestValue.reduce((a, b) => a + b, 0) / groupedData[clientCount].bestValue.length,
            avgMethodRunTime: groupedData[clientCount].methodRunTime.reduce((a, b) => a + b, 0) / groupedData[clientCount].methodRunTime.length,
            avgStartTimeClient: groupedData[clientCount].startTimeClient.reduce((a, b) => a + b, 0) / groupedData[clientCount].startTimeClient.length
        })).sort((a, b) => a.numClients - b.numClients);

        // График 1: Время выполнения и лучший результат
        const ctx1 = document.getElementById('performanceChart').getContext('2d');
        new Chart(ctx1, {
            type: 'line',
            data: {
                labels: chartData.map(d => d.numClients),
                datasets: [
                    {
                        label: 'Лучшее значение',
                        data: chartData.map(d => d.avgBestValue),
                        borderColor: 'rgba(54, 162, 235, 1)',
                        yAxisID: 'y1',
                        fill: false,
                        tension: 0.1
                    },
                    {
                        label: 'Время выполнения (с)',
                        data: chartData.map(d => d.avgMethodRunTime),
                        borderColor: 'rgba(255, 159, 64, 1)',
                        yAxisID: 'y2',
                        fill: false,
                        borderDash: [5, 5],
                        tension: 0.1
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    x: {
                        title: { display: true, text: 'Количество клиентов' }
                    },
                    y1: {
                        type: 'linear',
                        position: 'left',
                        title: { display: true, text: 'Лучшее значение' },
                        grid: { drawOnChartArea: false }
                    },
                    y2: {
                        type: 'linear',
                        position: 'right',
                        title: { display: true, text: 'Время выполнения (с)' },
                        grid: { drawOnChartArea: false }
                    }
                },
                plugins: {
                    title: { display: true, text: 'Время выполнения и лучший результат vs Количество клиентов' },
                    legend: { display: true }
                }
            }
        });

        // График 2: Время запуска клиентов
        const ctx2 = document.getElementById('startTimeChart').getContext('2d');
        new Chart(ctx2, {
            type: 'line',
            data: {
                labels: chartData.map(d => d.numClients),
                datasets: [
                    {
                        label: 'Время запуска клиентов (с)',
                        data: chartData.map(d => d.avgStartTimeClient),
                        borderColor: 'rgba(54, 162, 235, 1)',
                        fill: false,
                        tension: 0.1
                    }
                ]
            },
            options: {
                responsive: true,
                scales: {
                    x: {
                        title: { display: true, text: 'Количество клиентов' }
                    },
                    y: {
                        title: { display: true, text: 'Время запуска клиентов (с)' }
                    }
                },
                plugins: {
                    title: { display: true, text: 'Время запуска клиентов vs Количество клиентов' },
                    legend: { display: true }
                }
            }
        });
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




