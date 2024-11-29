## AntDist

  

#### Для удаленной сессии в Windows 8+

В данном приложении предполагается, что ПК клиентов находятся в одной локальной сети, могут иметь разные доменные имена.

Для запуска системы на разных компьютерах(удаленно) требуется выполнить предварительную настройку:

  
в config файле установить значения:
	`"nameClients": ["hostname","hostname"]`
	`"localtest": false`

1. на клиенте:

	`winrm quickconfig`
	
	Клиенты должны находится в общедоступной сети

  

2. на сервере:

	`winrm quickconfig`
	
	`winrm set winrm/config/client '@{TrustedHosts="DESKTOP-196D01,DESKTOP-19B6D0D"}'`
	
	Сервер должны находится в общедоступной сети (проверено только локально)


#### Настройки для сервера в config.json:
```JSON
{
    "nameClients": 2, //всего клиентов (как и для локального, так и для удаленного), если "localtest" = true == имена моугут быть пустыми или указывается цифрой

    "maxAnts": 20,

    "inPort": 6000, // с N порта вхоядшие

    "outport": 7000, // с N порта исходящие

    "username": "Admin", //не нужно, если "localtest" = true

    "password": "admin", //не нужно, если "localtest" = true

    "maxIteration": 200,

    "alpha": 1.0,

    "beta": 5.0,

    "Q": 100,

    "RHO": 0.1,

    "countSubjects": "1000",

    "pathToEXE": "C:\\temp", //не нужно, если "localtest" = true

    "nameFile": "Client.exe", //не нужно, если "localtest" = true

    "localtest": true, // тестирование через локальный адрес

    "uploadFile": true, // обеспечивает загрзку файла на удаленный компьютер    

}
```
