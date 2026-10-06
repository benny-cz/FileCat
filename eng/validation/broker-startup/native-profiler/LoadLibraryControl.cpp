#include <windows.h>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    HMODULE library = LoadLibraryW(argv[1]);
    if (!library) return 3;
    FreeLibrary(library);
    return 73;
}
