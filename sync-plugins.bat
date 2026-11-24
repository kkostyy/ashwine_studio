@echo off
rem Копирует plugins\*.cs в server\oxide\plugins — Oxide сам перезагрузит изменённые плагины.
copy /Y "%~dp0plugins\*.cs" "%~dp0server\oxide\plugins\" >nul
echo Плагины скопированы.
