@echo off
echo Stopping TMU Tarang Connect...
taskkill /IM TMUTarangConnect.exe /F 2>nul
if %ERRORLEVEL% EQU 0 (
    echo [OK] TMU Tarang Connect stopped successfully.
) else (
    echo [INFO] TMU Tarang Connect was not running.
)
timeout /t 3

