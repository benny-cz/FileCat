#include <windows.h>
#include <stdio.h>

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    wchar_t marker[32768];
    DWORD size = GetEnvironmentVariableW(L"FILECAT_OWNED_HOOK_MARKER", marker, 32768);
    if (!size || size >= 32768) return FALSE;
    SID_IDENTIFIER_AUTHORITY authority = SECURITY_NT_AUTHORITY;
    PSID admins = nullptr; BOOL administrator = FALSE;
    if (AllocateAndInitializeSid(&authority, 2, SECURITY_BUILTIN_DOMAIN_RID, DOMAIN_ALIAS_RID_ADMINS, 0,0,0,0,0,0,&admins)) {
        CheckTokenMembership(nullptr, admins, &administrator);
        FreeSid(admins);
    }
    char text[256];
    int bytes = sprintf_s(text, "{\"PID\":%lu,\"Administrator\":%s,\"SyntheticControl\":true,\"NativeDllAttachObserved\":true}", GetCurrentProcessId(), administrator ? "true" : "false");
    HANDLE file = CreateFileW(marker, GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return FALSE;
    DWORD written = 0; BOOL ok = WriteFile(file, text, bytes, &written, nullptr); CloseHandle(file);
    return ok && written == (DWORD)bytes;
}

extern "C" __declspec(dllexport) HRESULT __stdcall OwnedProfilerClassFactory(REFCLSID, REFIID, LPVOID* value)
{
    if (value) *value = nullptr;
    // A deliberately unavailable profiler factory: the control observes loading only and supplies no profiler callbacks.
    return CLASS_E_CLASSNOTAVAILABLE;
}
