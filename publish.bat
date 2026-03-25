@echo off
setlocal

set "ROOT=%~dp0"
set "PUBLISH_DIR=%ROOT%publish"
set "PROJECT=%ROOT%ImageViewer.App\ImageViewer.App.csproj"

echo ============================================
echo  ImageZeus - Publish
echo ============================================
echo.

if exist "%PUBLISH_DIR%" (
    echo Cleaning previous publish output...
    rmdir /s /q "%PUBLISH_DIR%"
)

echo Publishing self-contained (no runtime required)...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -o "%PUBLISH_DIR%" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=true -p:TrimMode=partial
if errorlevel 1 (
    echo.
    echo ERROR: Publish failed.
    pause
    exit /b 1
)

echo.
echo Copying icon...
copy /y "%ROOT%zeusicon.ico" "%PUBLISH_DIR%\zeusicon.ico" >nul
copy /y "%ROOT%zeusicon.png" "%PUBLISH_DIR%\zeusicon.png" >nul

echo.
echo ============================================
echo  Publish complete: %PUBLISH_DIR%
echo  Executable: %PUBLISH_DIR%\ImageZeus.exe
echo ============================================
echo.

