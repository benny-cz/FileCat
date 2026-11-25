// The administrator window launcher, FileCat.AdminLauncher.exe (release issue I644). AdministratorWindow starts this
// program, not FileCat.exe, with the shell's runas verb. UAC builds an elevated program's environment from the account's
// registry environment, which the ordinary account changes without elevation, and the .NET runtime takes its profiler,
// startup hook, host and diagnostics settings from the environment before any of FileCat's code runs. So the launcher
// removes those settings, as the administrator helper's native bootstrap does (I155), and only then starts the
// FileCat.exe beside it, with exactly the arguments it was given and the system folder as its working folder. It starts
// nothing else and takes no program from its arguments. It stays until that FileCat exits and returns its exit code, and
// becomes idle only after FileCat has, so the ordinary window follows the new window's startup and lifetime through this
// process as it followed FileCat.exe before.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <cwchar>

namespace
{
    constexpr DWORD PathLimit = 32768;
    constexpr int Refused = 2;

    // The runtime's own settings: the prefixes the helper's bootstrap removes, compared without case as Windows compares
    // environment names. The machine and account environments stay as they are; nothing here reads or logs a value.
    bool ClearRuntimeEnvironment()
    {
        LPWCH environment = GetEnvironmentStringsW();
        if (!environment) return false;
        std::vector<std::wstring> names;
        for (const wchar_t* entry = environment; *entry; entry += wcslen(entry) + 1)
        {
            const wchar_t* equal = wcschr(entry, L'=');
            if (!equal || equal == entry) continue; // Windows' hidden drive-current-directory entries.
            std::wstring name(entry, equal);
            for (const wchar_t* prefix : { L"DOTNET_", L"COMPLUS_", L"CORECLR_", L"COR_", L"COREHOST_", L"MONO_" })
                if (_wcsnicmp(name.c_str(), prefix, wcslen(prefix)) == 0) { names.push_back(name); break; }
        }
        FreeEnvironmentStringsW(environment);
        for (const auto& name : names)
            if (!SetEnvironmentVariableW(name.c_str(), nullptr)) return false;
        // No debugger, profiler or diagnostic port can attach to the administrator window later either.
        return SetEnvironmentVariableW(L"DOTNET_EnableDiagnostics", L"0") != FALSE;
    }

    // The arguments exactly as this program received them: its command line after the program name. The program name
    // ends at the first space or tab outside quotes, as the C runtime reads it, and so FileCat.exe's .NET host reads
    // the rest of the same text into the same arguments.
    const wchar_t* Arguments(const wchar_t* line)
    {
        bool quoted = false;
        for (; *line && (quoted || (*line != L' ' && *line != L'\t')); ++line)
            if (*line == L'"') quoted = !quoted;
        while (*line == L' ' || *line == L'\t') ++line;
        return line;
    }

    int Refuse(const std::wstring& reason)
    {
        MessageBoxW(nullptr, reason.c_str(), L"FileCat — administrator window", MB_OK | MB_ICONERROR | MB_TOPMOST);
        return Refused;
    }

    // The ordinary window waits for this process to become idle before it removes a request it staged for the new
    // window, and the new FileCat reads that request before it shows anything. So wait until FileCat is idle, has
    // exited, or turns out to have no message queue to wait for.
    void WaitForStartup(HANDLE process)
    {
        while (WaitForInputIdle(process, 200) == WAIT_TIMEOUT)
            if (WaitForSingleObject(process, 0) != WAIT_TIMEOUT) return;
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    try
    {
        // Anything loaded later comes from the system folder only, never from this folder, the working folder or PATH.
        if (!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32))
            return Refuse(L"FileCat could not limit where its administrator launcher loads code from. Nothing was started.");
        std::vector<wchar_t> module(PathLimit), system(PathLimit);
        DWORD count = GetModuleFileNameW(nullptr, module.data(), PathLimit);
        if (!count || count >= PathLimit) return Refuse(L"FileCat's administrator launcher could not find its own folder. Nothing was started.");
        std::wstring self(module.data(), count);
        auto split = self.find_last_of(L'\\');
        if (split == std::wstring::npos) return Refuse(L"FileCat's administrator launcher could not find its own folder. Nothing was started.");
        std::wstring target = self.substr(0, split) + L"\\FileCat.exe";
        count = GetSystemDirectoryW(system.data(), PathLimit);
        if (!count || count >= PathLimit || !SetCurrentDirectoryW(system.data()))
            return Refuse(L"FileCat's administrator launcher could not use the Windows system folder. Nothing was started.");
        if (!ClearRuntimeEnvironment())
            return Refuse(L"FileCat's administrator launcher could not remove the .NET runtime settings from its environment. Nothing was started.");

        std::wstring command = L"\"" + target + L"\"";
        const wchar_t* arguments = Arguments(GetCommandLineW());
        if (*arguments) command.append(L" ").append(arguments);
        std::vector<wchar_t> commandLine(command.begin(), command.end());
        commandLine.push_back(L'\0');

        STARTUPINFOW own{};
        own.cb = sizeof(own);
        GetStartupInfoW(&own);
        STARTUPINFOW startup{};
        startup.cb = sizeof(startup);
        if (own.dwFlags & STARTF_USESHOWWINDOW)
        {
            startup.dwFlags = STARTF_USESHOWWINDOW;
            startup.wShowWindow = own.wShowWindow;
        }
        PROCESS_INFORMATION child{};
        // No inherited handles and no environment block: FileCat gets this process's environment, cleaned above.
        if (!CreateProcessW(target.c_str(), commandLine.data(), nullptr, nullptr, FALSE, CREATE_SUSPENDED, nullptr,
                system.data(), &startup, &child))
        {
            DWORD error = GetLastError();
            return Refuse(L"FileCat could not start " + target + L" as administrator (Windows error " +
                std::to_wstring(error) + L"). Reinstall FileCat if the file is missing.");
        }
        // The person asked for this window: let it come to the front as FileCat.exe started directly would.
        AllowSetForegroundWindow(child.dwProcessId);
        if (ResumeThread(child.hThread) == static_cast<DWORD>(-1))
        {
            TerminateProcess(child.hProcess, Refused);
            CloseHandle(child.hThread);
            CloseHandle(child.hProcess);
            return Refuse(L"FileCat's administrator launcher could not start FileCat. Nothing was run.");
        }
        CloseHandle(child.hThread);

        WaitForStartup(child.hProcess);
        // Waiting for messages makes this process idle, which ends the ordinary window's wait for it.
        MSG message;
        PeekMessageW(&message, nullptr, 0, 0, PM_NOREMOVE);
        for (;;)
        {
            DWORD wait = MsgWaitForMultipleObjectsEx(1, &child.hProcess, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
            if (wait == WAIT_OBJECT_0 + 1)
            {
                while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) DispatchMessageW(&message);
                continue;
            }
            if (wait != WAIT_OBJECT_0) WaitForSingleObject(child.hProcess, INFINITE);
            break;
        }
        DWORD code = Refused;
        if (!GetExitCodeProcess(child.hProcess, &code)) code = Refused;
        CloseHandle(child.hProcess);
        return static_cast<int>(code);
    }
    catch (...) { return Refused; }
}
