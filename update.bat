@echo off
chcp 65001 >nul
rem Обновление сервера Rust и Oxide (после вайпа / патча Facepunch). Плагины и данные не трогаются.
"%~dp0steamcmd\steamcmd.exe" +force_install_dir "%~dp0server" +login anonymous +app_update 258550 +quit
powershell -NoProfile -ExecutionPolicy Bypass -Command "Invoke-WebRequest 'https://umod.org/games/rust/download?tag=public' -OutFile '%~dp0oxide.zip'; Expand-Archive '%~dp0oxide.zip' -DestinationPath '%~dp0server' -Force; Remove-Item '%~dp0oxide.zip'"
echo Готово. Запускай start.bat
pause
