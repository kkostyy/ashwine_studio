@echo off
chcp 65001 >nul
if "%RCON_PASS%"=="" set RCON_PASS=CHANGE_ME
cd /d "%~dp0server"
if not exist "..\logs" mkdir "..\logs"
echo Сервер запускается. Лог: logs\server.log   Подключение: F1 ^> client.connect localhost:28015
RustDedicated.exe -batchmode -nographics -disable-server-occlusion ^
 +server.identity "hub" ^
 +server.port 28015 +server.queryport 28017 ^
 +rcon.port 28016 +rcon.password "%RCON_PASS%" +rcon.web 1 ^
 +server.level "Procedural Map" +server.seed 2026 +server.worldsize 6000 ^
 +server.maxplayers 50 ^
 +server.secure 0 ^
 +server.hostname "ASH & WINE | Studio" ^
 +server.description "Хаб: стройка, съёмки, ранкед, арены, аим-трен" ^
 +server.saveinterval 300 ^
 -logfile "..\logs\server.log"
pause
