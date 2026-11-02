@echo off
setlocal
cd /d "%~dp0"

set "PROJECT=src\SfmPatcher.App\ModernSfmPatcher.csproj"
set "OUTPUT=release\Modern-SFM-Patcher"
set "ZIP=release\Modern-SFM-Patcher-v0.1.0-win-x64.zip"

if not exist "%PROJECT%" (
    echo Project not found: %PROJECT%
    goto :failed
)

if not exist "release" mkdir "release"
tasklist /FI "IMAGENAME eq ModernSfmPatcher.exe" | find /I "ModernSfmPatcher.exe" >nul
if not errorlevel 1 (
    echo Close Modern SFM Patcher before creating a release.
    goto :failed
)

if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"
mkdir "%OUTPUT%"

echo Publishing self-contained x64 application...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableMsixTooling=true -p:IncludeAllContentForSelfExtract=true -p:DebugSymbols=false -p:DebugType=None -o "%OUTPUT%"
if errorlevel 1 goto :failed

echo Copying application assets...
xcopy "src\SfmPatcher.App\Assets" "%OUTPUT%\Assets" /E /I /Y >nul
if errorlevel 2 goto :failed

echo Creating release ZIP...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%OUTPUT%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 goto :failed

echo.
echo Release complete:
echo   Application: %OUTPUT%\ModernSfmPatcher.exe
echo   ZIP archive: %ZIP%
pause
exit /b 0

:failed
echo.
echo Release failed. Check the error above.
pause
exit /b 1
