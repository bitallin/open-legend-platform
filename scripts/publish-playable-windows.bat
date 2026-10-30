@echo off
setlocal
cd /d "%~dp0.."
dotnet publish client\Legend.Playable\Legend.Playable.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist\windows-player
if errorlevel 1 exit /b 1
echo OK: dist\windows-player\LegendClient.exe
echo Run: dist\windows-player\LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01
