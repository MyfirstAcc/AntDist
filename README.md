# AntDist

#### Для удаленной сессии в windows
Компьютеры не в одном домене.
на клиенте:
`winrm quickconfig`
на сервере:
`winrm quickconfig`
`winrm set winrm/config/client '@{TrustedHosts="DESKTOP-196D01,DESKTOP-19B6D0D"}'`