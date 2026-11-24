// The administrator launcher tests' stand-in for FileCat.exe (I644). It ships in no package and opens no window.
// It writes what it received to stand-in-record.json beside itself: its process ID, its command line, the arguments
// the C runtime parsed from that command line (as FileCat.exe's .NET host parses its own), its environment and its
// working folder. stand-in-settings.txt beside it may change what it does next: with idle-delay-ms=N it starts slowly,
// waiting N ms, noting the time, then waiting for messages, which is what makes a Windows program idle, for stay-ms
// (3000 by default) before it notes the time again and exits; exit-code=N sets its exit code. Its settings never come
// from the environment, which is what the tests examine.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <vector>
#include <cwchar>
#include <cstdio>
#include <cstring>

namespace
{
    struct Settings
    {
        DWORD Delay = 0;
        DWORD Stay = 3000;
        int Code = 0;
    };

    Settings ReadSettings(const std::wstring& folder)
    {
        Settings settings;
        std::wstring path = folder + L"\\stand-in-settings.txt";
        HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return settings;
        char text[512]{};
        DWORD read = 0;
        if (!ReadFile(file, text, sizeof text - 1, &read, nullptr)) read = 0;
        CloseHandle(file);
        text[read] = 0;
        for (char* line = text; line && *line;)
        {
            char* next = strchr(line, '\n');
            if (next) *next++ = 0;
            unsigned long value = 0;
            if (sscanf_s(line, "idle-delay-ms=%lu", &value) == 1) settings.Delay = value;
            else if (sscanf_s(line, "stay-ms=%lu", &value) == 1) settings.Stay = value;
            else if (sscanf_s(line, "exit-code=%lu", &value) == 1) settings.Code = static_cast<int>(value);
            line = next;
        }
        return settings;
    }

    // A JSON string in printable ASCII: everything else as \u escapes of its UTF-16 code units.
    std::string Json(const wchar_t* text)
    {
        std::string json = "\"";
        for (; *text; ++text)
        {
            wchar_t c = *text;
            if (c == L'"' || c == L'\\')
            {
                json += '\\';
                json += static_cast<char>(c);
            }
            else if (c >= 0x20 && c < 0x7F) json += static_cast<char>(c);
            else
            {
                char escaped[8];
                snprintf(escaped, sizeof escaped, "\\u%04x", static_cast<unsigned>(c));
                json += escaped;
            }
        }
        return json + "\"";
    }

    // UTC in 100-nanosecond units since 1601, as DateTime.FromFileTimeUtc reads it.
    unsigned long long Now()
    {
        FILETIME now;
        GetSystemTimePreciseAsFileTime(&now);
        return (static_cast<unsigned long long>(now.dwHighDateTime) << 32) | now.dwLowDateTime;
    }

    // Replaces the record in one step, so a reader never sees half of one.
    bool Write(const std::wstring& folder, const std::string& json)
    {
        std::wstring temporary = folder + L"\\stand-in-record.tmp", record = folder + L"\\stand-in-record.json";
        HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        DWORD written = 0;
        bool complete = WriteFile(file, json.data(), static_cast<DWORD>(json.size()), &written, nullptr) && written == json.size();
        complete = CloseHandle(file) && complete;
        return complete && MoveFileExW(temporary.c_str(), record.c_str(), MOVEFILE_REPLACE_EXISTING);
    }
}

int wmain(int argc, wchar_t** argv)
{
    std::vector<wchar_t> module(32768), directory(32768);
    DWORD length = GetModuleFileNameW(nullptr, module.data(), 32768);
    if (!length || length >= 32768) return 90;
    std::wstring folder(module.data(), length);
    auto split = folder.find_last_of(L'\\');
    if (split == std::wstring::npos) return 90;
    folder.resize(split);
    Settings settings = ReadSettings(folder);

    std::string json = "{\"pid\":" + std::to_string(GetCurrentProcessId());
    json += ",\"commandLine\":" + Json(GetCommandLineW());
    json += ",\"arguments\":[";
    for (int index = 1; index < argc; ++index)
    {
        if (index > 1) json += ",";
        json += Json(argv[index]);
    }
    json += "],\"environment\":[";
    LPWCH environment = GetEnvironmentStringsW();
    for (const wchar_t* entry = environment; entry && *entry; entry += wcslen(entry) + 1)
    {
        if (entry != environment) json += ",";
        json += Json(entry);
    }
    if (environment) FreeEnvironmentStringsW(environment);
    length = GetCurrentDirectoryW(32768, directory.data());
    json += "],\"currentDirectory\":" + Json(length && length < 32768 ? directory.data() : L"");
    if (!settings.Delay) return Write(folder, json + "}") ? settings.Code : 90;

    Sleep(settings.Delay);
    json += ",\"idleAt\":" + std::to_string(Now());
    if (!Write(folder, json + "}")) return 90;
    // GetMessage with nothing to read makes this program idle; a thread timer ends the wait.
    MSG message;
    UINT_PTR timer = SetTimer(nullptr, 0, settings.Stay, nullptr);
    while (timer && GetMessageW(&message, nullptr, 0, 0) > 0 && message.message != WM_TIMER) DispatchMessageW(&message);
    json += ",\"exitingAt\":" + std::to_string(Now());
    return Write(folder, json + "}") ? settings.Code : 90;
}
