@echo off
REM Builds a single self-contained LocalMind.exe into .\publish
cd /d "%~dp0"
dotnet publish src\LocalMind\LocalMind.vbproj -c Release -r win-x64 -p:PublishSingleFile=true -o publish
if errorlevel 1 (echo Build failed & pause & exit /b 1)
echo.
echo Done: %cd%\publish\LocalMind.exe
pause
