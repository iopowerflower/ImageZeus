/*
 * ImageZeus Native Launcher
 *
 * Tiny native exe (~10KB) that either sends a file path to the running
 * daemon via named pipe, or starts the daemon if it isn't running.
 * Avoids .NET runtime bootstrap cost for the common "open an image" path.
 *
 * Compile: cl /O1 /Fe:ImageZeus.exe ImageZeusLauncher.c /link /SUBSYSTEM:WINDOWS
 *          kernel32.lib advapi32.lib shell32.lib
 */

#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shellapi.h>

#define MUTEX_NAME  L"ImageZeus_Singleton_Mutex"
#define PIPE_NAME   L"\\\\.\\pipe\\ImageZeus_Pipe"
#define DAEMON_EXE  L"ImageZeusDaemon.exe"

static BOOL SendPathViaPipe(const WCHAR *path)
{
    HANDLE hPipe = CreateFileW(PIPE_NAME, GENERIC_WRITE, 0, NULL,
                               OPEN_EXISTING, 0, NULL);
    if (hPipe == INVALID_HANDLE_VALUE)
        return FALSE;

    /* Convert wide path to UTF-8 for the .NET StreamReader on the other end */
    int utf8Len = WideCharToMultiByte(CP_UTF8, 0, path, -1, NULL, 0, NULL, NULL);
    if (utf8Len <= 0) { CloseHandle(hPipe); return FALSE; }

    char buf[2048];
    if (utf8Len > (int)(sizeof(buf) - 2)) { CloseHandle(hPipe); return FALSE; }

    WideCharToMultiByte(CP_UTF8, 0, path, -1, buf, sizeof(buf), NULL, NULL);

    /* Replace null terminator with newline (StreamReader.ReadLine expects \n) */
    int strLen = utf8Len - 1;
    buf[strLen] = '\n';
    strLen++;

    DWORD written;
    WriteFile(hPipe, buf, (DWORD)strLen, &written, NULL);
    FlushFileBuffers(hPipe);
    CloseHandle(hPipe);
    return TRUE;
}

static void LaunchDaemon(const WCHAR *filePath)
{
    /* Build path to daemon exe in same directory as this launcher */
    WCHAR exePath[MAX_PATH];
    GetModuleFileNameW(NULL, exePath, MAX_PATH);

    /* Replace filename with daemon exe name */
    WCHAR *lastSlash = exePath;
    for (WCHAR *p = exePath; *p; p++)
        if (*p == L'\\') lastSlash = p;
    *(lastSlash + 1) = L'\0';

    WCHAR daemonPath[MAX_PATH];
    lstrcpyW(daemonPath, exePath);
    lstrcatW(daemonPath, DAEMON_EXE);

    /* Build command line: "daemonPath" "filePath" */
    WCHAR cmdLine[4096];
    if (filePath && filePath[0])
        wsprintfW(cmdLine, L"\"%s\" \"%s\"", daemonPath, filePath);
    else
        wsprintfW(cmdLine, L"\"%s\"", daemonPath);

    STARTUPINFOW si;
    PROCESS_INFORMATION pi;
    ZeroMemory(&si, sizeof(si));
    si.cb = sizeof(si);
    ZeroMemory(&pi, sizeof(pi));

    CreateProcessW(NULL, cmdLine, NULL, NULL, FALSE,
                   DETACHED_PROCESS, NULL, NULL, &si, &pi);

    if (pi.hThread) CloseHandle(pi.hThread);
    if (pi.hProcess) CloseHandle(pi.hProcess);
}

int WINAPI wWinMain(HINSTANCE hInstance, HINSTANCE hPrev,
                    LPWSTR lpCmdLine, int nCmdShow)
{
    (void)hInstance; (void)hPrev; (void)nCmdShow;

    /* Extract file path from command line args */
    int argc;
    LPWSTR *argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    const WCHAR *filePath = NULL;

    for (int i = 1; i < argc; i++)
    {
        if (argv[i][0] != L'-')
        {
            filePath = argv[i];
            break;
        }
    }

    /* Try to open existing mutex (don't create one) */
    HANDLE hMutex = OpenMutexW(SYNCHRONIZE, FALSE, MUTEX_NAME);

    if (hMutex)
    {
        /* Daemon is running — send path via pipe */
        CloseHandle(hMutex);

        if (filePath)
            SendPathViaPipe(filePath);
        else
            SendPathViaPipe(L"");  /* Open empty window */
    }
    else
    {
        /* Daemon not running — start it */
        LaunchDaemon(filePath);
    }

    if (argv) LocalFree(argv);
    return 0;
}
