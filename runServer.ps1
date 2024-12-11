$max_runs = 10
$count = 1
$programPath = ".\Server.exe"
$arguments = "-delay=no"

while ($count -le $max_runs) {
    Write-Host "Запуск программы $count раз..."
    Start-Process -FilePath $programPath -ArgumentList $arguments -Verb RunAs -Wait
    $count++
}
Write-Host "Программа завершена."
