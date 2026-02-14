@echo off
cd /d d:\ZeroHourStudio
dotnet build ZeroHourStudio.UI.WPF\ZeroHourStudio.UI.WPF.csproj -c Debug --no-restore
if %errorlevel% neq 0 pause
