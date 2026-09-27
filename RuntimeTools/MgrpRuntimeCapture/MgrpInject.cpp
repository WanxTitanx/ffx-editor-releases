#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

static void usage() {
    fprintf(stderr, "Usage: MgrpInject.exe <pid> <absolute-dll-path>\n");
}

int main(int argc, char **argv) {
    if (argc != 3) {
        usage();
        return 2;
    }

    DWORD pid = (DWORD)strtoul(argv[1], NULL, 10);
    const char *dll_path = argv[2];
    if (!pid || !dll_path || !dll_path[0]) {
        usage();
        return 2;
    }

    DWORD access = PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
        PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ;
    HANDLE process = OpenProcess(access, FALSE, pid);
    if (!process) {
        fprintf(stderr, "OpenProcess failed: %lu\n", GetLastError());
        return 3;
    }

    SIZE_T bytes = strlen(dll_path) + 1;
    void *remote = VirtualAllocEx(process, NULL, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        fprintf(stderr, "VirtualAllocEx failed: %lu\n", GetLastError());
        CloseHandle(process);
        return 4;
    }

    SIZE_T written = 0;
    if (!WriteProcessMemory(process, remote, dll_path, bytes, &written) || written != bytes) {
        fprintf(stderr, "WriteProcessMemory failed: %lu written=%zu expected=%zu\n",
                GetLastError(), (size_t)written, (size_t)bytes);
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return 5;
    }

    HMODULE kernel32 = GetModuleHandleA("kernel32.dll");
    FARPROC load_library = kernel32 ? GetProcAddress(kernel32, "LoadLibraryA") : NULL;
    if (!load_library) {
        fprintf(stderr, "GetProcAddress(LoadLibraryA) failed: %lu\n", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return 6;
    }

    HANDLE thread = CreateRemoteThread(process, NULL, 0,
                                       (LPTHREAD_START_ROUTINE)load_library,
                                       remote, 0, NULL);
    if (!thread) {
        fprintf(stderr, "CreateRemoteThread failed: %lu\n", GetLastError());
        VirtualFreeEx(process, remote, 0, MEM_RELEASE);
        CloseHandle(process);
        return 7;
    }

    WaitForSingleObject(thread, 10000);
    DWORD exit_code = 0;
    GetExitCodeThread(thread, &exit_code);
    printf("LoadLibraryA exit_code=0x%08lX remote_path=%p\n", exit_code, remote);

    CloseHandle(thread);
    VirtualFreeEx(process, remote, 0, MEM_RELEASE);
    CloseHandle(process);
    return exit_code ? 0 : 8;
}
