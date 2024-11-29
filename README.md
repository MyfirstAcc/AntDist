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
    "nameClients": 2,
    "maxAnts": 20,
    "inPort": 6000,
    "outport": 7000,
    "username": "Admin",
    "password": "admin",
    "maxIteration": 200,
    "alpha": 1.0,
    "beta": 5.0,
    "Q": 100,
    "RHO": 0.1,
    "countSubjects": "1000",
    "pathToEXE": "C:\\temp",
    "nameFile": "Client.exe",
    "localtest": true,
    "uploadFile": true
}
```
