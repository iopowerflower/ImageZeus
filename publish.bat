@echo off
setlocal

set "ROOT=%~dp0"
set "PUBLISH_DIR=%ROOT%publish"
set "PROJECT=%ROOT%ImageViewer.App\ImageViewer.App.csproj"
set "LAUNCHER_DIR=%ROOT%launcher"

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
echo ============================================
echo  Building native launcher...
echo ============================================
echo.

:: Find vcvarsall.bat
set "VCVARS="
for %%V in (
    "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
    "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvarsall.bat"
) do (
    if exist %%V (
        set "VCVARS=%%~V"
        goto :found_vcvars
    )
)

echo WARNING: Could not find Visual Studio C compiler (vcvarsall.bat).
echo The native launcher was NOT built. File associations will use the
echo .NET daemon directly (slower cold-start).
echo.
echo To build the native launcher, install Visual Studio Build Tools:
echo   https://visualstudio.microsoft.com/visual-cpp-build-tools/
echo.
goto :skip_launcher

:found_vcvars
echo Using: %VCVARS%
call "%VCVARS%" x64 >nul 2>&1

:: Compile resource (icon)
rc /nologo /fo "%LAUNCHER_DIR%\launcher.res" "%LAUNCHER_DIR%\launcher.rc"
if errorlevel 1 (
    echo WARNING: Resource compiler failed, building without icon.
    set "RES_FILE="
) else (
    set "RES_FILE=%LAUNCHER_DIR%\launcher.res"
)

:: Compile launcher
cl /nologo /O1 /W3 /Fe:"%PUBLISH_DIR%\ImageZeus.exe" "%LAUNCHER_DIR%\ImageZeusLauncher.c" %RES_FILE% /link /SUBSYSTEM:WINDOWS kernel32.lib advapi32.lib shell32.lib user32.lib
if errorlevel 1 (
    echo.
    echo WARNING: Native launcher build failed.
    echo File associations will use the .NET daemon directly.
    goto :skip_launcher
)

:: Clean up intermediate files
del /q "%LAUNCHER_DIR%\launcher.res" 2>nul
del /q "%LAUNCHER_DIR%\ImageZeusLauncher.obj" 2>nul

echo Native launcher built successfully.

:skip_launcher

echo.
echo Copying icon...
copy /y "%ROOT%zeusicon.ico" "%PUBLISH_DIR%\zeusicon.ico" >nul
copy /y "%ROOT%zeusicon.png" "%PUBLISH_DIR%\zeusicon.png" >nul

echo.
echo ============================================
echo  Publish complete: %PUBLISH_DIR%
echo  Launcher:  %PUBLISH_DIR%\ImageZeus.exe
echo  Daemon:    %PUBLISH_DIR%\ImageZeusDaemon.exe
echo ============================================
echo.
