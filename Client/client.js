const serverUrl = "ws://127.0.0.1:6000/";
const socket = new WebSocket(serverUrl);

socket.onopen = () => {
    console.log("Соединение установлено!");
    socket.send("Привет, сервер!");
};

socket.onmessage = (event) => {
    console.log("Сообщение от сервера:", event.data);
};

socket.onclose = (event) => {
    console.log("Соединение закрыто.", event);
};

socket.onerror = (error) => {
    console.error("Ошибка WebSocket:", error);
};