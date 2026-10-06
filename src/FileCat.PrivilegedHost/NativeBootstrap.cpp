// The managed helper's checks run after CLR initialization. Establish its loader boundary first.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <string>
#include <vector>
#include <cwchar>
#include <algorithm>

namespace
{
    using RunHost = int (__cdecl *)(int, const wchar_t**, const wchar_t*, const wchar_t*, const wchar_t*);
    constexpr DWORD PathLimit = 32768;

    std::wstring FinalPath(const std::wstring& path)
    {
        HANDLE file = CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr);
        if (file == INVALID_HANDLE_VALUE) return {};
        std::vector<wchar_t> buffer(PathLimit);
        DWORD count = GetFinalPathNameByHandleW(file, buffer.data(), PathLimit, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        CloseHandle(file);
        if (!count || count >= PathLimit) return {};
        std::wstring result(buffer.data(), count);
        if (result.rfind(L"\\\\?\\UNC\\", 0) == 0) return {};
        if (result.rfind(L"\\\\?\\", 0) == 0) result.erase(0, 4);
        return result;
    }

    std::wstring KnownFolder(REFKNOWNFOLDERID id)
    {
        PWSTR folder = nullptr;
        if (FAILED(SHGetKnownFolderPath(id, KF_FLAG_DEFAULT, nullptr, &folder))) return {};
        std::wstring result = FinalPath(folder);
        CoTaskMemFree(folder);
        return result;
    }

    bool Protected(const std::wstring& path, const std::wstring& root)
    {
        auto final = FinalPath(path);
        static const auto x86 = KnownFolder(FOLDERID_ProgramFilesX86);
        for (const auto& folder : { root, x86 })
            if (!folder.empty() && final.size() > folder.size() && final[folder.size()] == L'\\' &&
                _wcsnicmp(final.c_str(), folder.c_str(), folder.size()) == 0) return true;
        return false;
    }

    bool ClearRuntimeEnvironment()
    {
        // Snapshot names before deleting them. Never change machine/user environment or log its values.
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
        // Disable diagnostic attachment as well as startup profiling in this process.
        return SetEnvironmentVariableW(L"DOTNET_EnableDiagnostics", L"0") != FALSE;
    }

    struct Version { unsigned major = 0, minor = 0, patch = 0; };
    bool StableVersion(const wchar_t* text, Version& version)
    {
        for (unsigned* part : { &version.major, &version.minor, &version.patch })
        {
            if (*text < L'0' || *text > L'9') return false;
            unsigned value = 0;
            while (*text >= L'0' && *text <= L'9')
            {
                unsigned digit = *text++ - L'0';
                if (value > (UINT_MAX - digit) / 10) return false;
                value = value * 10 + digit;
            }
            *part = value;
            if (part != &version.patch && *text++ != L'.') return false;
        }
        return *text == 0 && version.major >= 10;
    }
    bool Later(const Version& left, const Version& right)
    {
        if (left.major != right.major) return left.major > right.major;
        if (left.minor != right.minor) return left.minor > right.minor;
        return left.patch > right.patch;
    }

    std::wstring InstalledHost(const std::wstring& root, const std::wstring& programFiles)
    {
        // FDD servicing remains in the administrator-protected .NET installation. No environment/user root.
        auto base = root + L"\\host\\fxr\\";
        WIN32_FIND_DATAW entry{};
        HANDLE search = FindFirstFileW((base + L"*").c_str(), &entry);
        if (search == INVALID_HANDLE_VALUE) return {};
        Version best{};
        std::wstring selected;
        do
        {
            Version version{};
            if (!(entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) || !StableVersion(entry.cFileName, version)) continue;
            auto path = base + entry.cFileName + L"\\hostfxr.dll";
            if (Later(version, best) && Protected(path, programFiles)) { best = version; selected = path; }
        } while (FindNextFileW(search, &entry));
        FindClose(search);
        return selected;
    }

    int Refuse(const wchar_t* reason)
    {
        MessageBoxW(nullptr, reason, L"FileCat — administrator helper", MB_OK | MB_ICONERROR | MB_TOPMOST);
        return 2;
    }
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    try
    {
        std::vector<wchar_t> module(PathLimit), system(PathLimit);
        DWORD count = GetModuleFileNameW(nullptr, module.data(), PathLimit);
        if (!count || count >= PathLimit) return 2;
        auto self = FinalPath(std::wstring(module.data(), count));
        auto root = KnownFolder(FOLDERID_ProgramFiles);
        if (!Protected(self, root)) return Refuse(L"The administrator helper runs only from the installed program folder (Program Files). Nothing was run.");
        auto split = self.find_last_of(L'\\');
        if (split == std::wstring::npos) return 2;
        auto directory = self.substr(0, split);
        auto app = directory + L"\\FileCat.PrivilegedHost.dll";
        for (const auto& name : { L"FileCat.PrivilegedHost.dll", L"FileCat.PrivilegedHost.deps.json", L"FileCat.PrivilegedHost.runtimeconfig.json" })
            if (!Protected(directory + L"\\" + name, root)) return Refuse(L"The administrator helper's application files are missing or outside its protected program folder. Reinstall FileCat. Nothing was run.");
        count = GetSystemDirectoryW(system.data(), PathLimit);
        if (!count || count >= PathLimit || !SetCurrentDirectoryW(system.data()) || !SetDllDirectoryW(L"") ||
            !SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32) ||
            !SetEnvironmentVariableW(L"PATH", system.data()) || !ClearRuntimeEnvironment())
            return Refuse(L"The administrator helper could not establish a safe loader environment. Nothing was run.");

        std::wstring dotnetRoot = directory;
        auto host = directory + L"\\hostfxr.dll";
        if (GetFileAttributesW(host.c_str()) == INVALID_FILE_ATTRIBUTES)
        {
            dotnetRoot = root + L"\\dotnet";
#if defined(_M_X64)
            SYSTEM_INFO native{}; GetNativeSystemInfo(&native);
            if (native.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_ARM64) dotnetRoot += L"\\x64";
#endif
            host = InstalledHost(dotnetRoot, root);
        }
        if (host.empty() || !Protected(host, root)) return Refuse(L"The administrator helper needs a protected .NET 10 installation. Install the Windows .NET runtime or reinstall FileCat. Nothing was run.");
        HMODULE library = LoadLibraryExW(host.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (!library) return Refuse(L"The administrator helper could not load its protected .NET host. Nothing was run.");
        auto run = reinterpret_cast<RunHost>(GetProcAddress(library, "hostfxr_main_startupinfo"));
        if (!run) return Refuse(L"The administrator helper's .NET host does not support this build. Nothing was run.");
        int argc = 0;
        LPWSTR* argv = CommandLineToArgvW(GetCommandLineW(), &argc);
        if (!argv) return 2;
        int result = run(argc, const_cast<const wchar_t**>(argv), self.c_str(), dotnetRoot.c_str(), app.c_str());
        LocalFree(argv);
        // CoreCLR owns process-wide state after initialization; do not unload its host.
        return result;
    }
    catch (...) { return 2; }
}
