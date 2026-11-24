# Установка тестового Rust-сервера с Oxide и плагинами хаба.
# Запуск: двойной клик по install.bat (или: powershell -ExecutionPolicy Bypass -File install.ps1)
$ErrorActionPreference = "Stop"
$Root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$SteamCmd = Join-Path $Root "steamcmd"
$Server   = Join-Path $Root "server"
$Identity = "hub"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Step($t) { Write-Host ""; Write-Host "=== $t ===" -ForegroundColor Magenta }

Step "1/6 SteamCMD"
if (-not (Test-Path "$SteamCmd\steamcmd.exe")) {
    New-Item -ItemType Directory -Force -Path $SteamCmd | Out-Null
    Invoke-WebRequest "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" -OutFile "$SteamCmd\steamcmd.zip"
    Expand-Archive "$SteamCmd\steamcmd.zip" -DestinationPath $SteamCmd -Force
    Remove-Item "$SteamCmd\steamcmd.zip"
} else { Write-Host "уже есть" }

Step "2/6 Rust Dedicated Server (~8-10 ГБ, первый раз долго)"
New-Item -ItemType Directory -Force -Path $Server | Out-Null
& "$SteamCmd\steamcmd.exe" +force_install_dir "$Server" +login anonymous +app_update 258550 validate +quit
if (-not (Test-Path "$Server\RustDedicated.exe")) { throw "RustDedicated.exe не найден — SteamCMD не докачал сервер. Запусти install.bat ещё раз." }

Step "3/6 Oxide (uMod)"
$oxZip = Join-Path $Root "oxide.zip"
Invoke-WebRequest "https://umod.org/games/rust/download?tag=public" -OutFile $oxZip
Expand-Archive $oxZip -DestinationPath $Server -Force
Remove-Item $oxZip

Step "4/6 Плагины"
$Plugins = Join-Path $Server "oxide\plugins"
New-Item -ItemType Directory -Force -Path $Plugins | Out-Null
Copy-Item "$Root\plugins\*.cs" $Plugins -Force
try {
    Invoke-WebRequest "https://umod.org/plugins/CopyPaste.cs" -OutFile "$Plugins\CopyPaste.cs"
    Write-Host "CopyPaste.cs скачан (нужен для Blueprints и рейд-теста)"
} catch { Write-Host "Не удалось скачать CopyPaste.cs — скачай вручную с umod.org/plugins/copy-paste" -ForegroundColor Yellow }
Get-ChildItem $Plugins -Filter *.cs | ForEach-Object { Write-Host "  " $_.Name }

Step "5/6 Админ (владелец сервера)"
$cfgDir = Join-Path $Server "server\$Identity\cfg"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
$steamId = Read-Host "Введи свой SteamID64 (17 цифр, начинается с 7656…; узнать: steamid.io). Enter — пропустить"
if ($steamId -match '^7656\d{13}$') {
    Set-Content -Path "$cfgDir\users.cfg" -Value "ownerid $steamId `"owner`" `"hub owner`"" -Encoding ASCII
    Write-Host "Записан владелец $steamId"
} else { Write-Host "Пропущено — потом в консоли сервера: ownerid <SteamID64>  и  server.writecfg" -ForegroundColor Yellow }

Step "6/6 Готово"
New-Item -ItemType Directory -Force -Path (Join-Path $Root "logs") | Out-Null
Write-Host "Запусти start.bat. Подключение из игры: F1 -> client.connect localhost:28015" -ForegroundColor Green
