#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>

static const uintptr_t IDB_IMAGE_BASE = 0x00400000;
static const uintptr_t VA_ROMREAD_PTR = 0x02310C40;
static const uintptr_t VA_SIZE_PTR = 0x02310C54;
static const uintptr_t VA_PATH_PTR = 0x02310C98;

static const char *OUT_DIR = "C:\\Users\\wande\\Documents\\ffx-editor-main\\work\\mgrp_runtime_capture";
static const char *LOG_PATH = "C:\\Users\\wande\\Documents\\ffx-editor-main\\work\\mgrp_runtime_capture\\mgrp_capture_passive.log";

typedef int (__cdecl *RomReadFn)(int resource, void *buffer, void *callback, void *context);
typedef int (__cdecl *SizeFn)(int resource);
typedef const char *(__cdecl *PathFn)(int resource);

struct CaptureJob {
    unsigned serial;
    int resource;
    void *buffer;
    unsigned size;
    DWORD delay_ms;
    char path[260];
};

static uintptr_t g_ffx_base;
static RomReadFn g_original_romread;
static LONG g_serial;

static uintptr_t rva(uintptr_t va) {
    return va - IDB_IMAGE_BASE;
}

static void ensure_dirs() {
    CreateDirectoryA("C:\\Users\\wande\\Documents\\ffx-editor-main\\work", NULL);
    CreateDirectoryA(OUT_DIR, NULL);
}

static void log_line(const char *fmt, ...) {
    ensure_dirs();
    FILE *f = NULL;
    fopen_s(&f, LOG_PATH, "ab");
    if (!f) {
        return;
    }
    SYSTEMTIME st;
    GetLocalTime(&st);
    fprintf(f, "%04u-%02u-%02u %02u:%02u:%02u.%03u ",
            st.wYear, st.wMonth, st.wDay, st.wHour, st.wMinute, st.wSecond, st.wMilliseconds);
    va_list ap;
    va_start(ap, fmt);
    vfprintf(f, fmt, ap);
    va_end(ap);
    fputc('\n', f);
    fclose(f);
}

static int safe_size_for_resource(int resource) {
    int size = 0;
    __try {
        SizeFn fn = *(SizeFn *)(g_ffx_base + rva(VA_SIZE_PTR));
        if (fn) {
            size = fn(resource);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        size = 0;
    }
    return size;
}

static void safe_path_for_resource(int resource, char *out, size_t out_size) {
    if (!out || out_size == 0) {
        return;
    }
    out[0] = 0;
    __try {
        PathFn fn = *(PathFn *)(g_ffx_base + rva(VA_PATH_PTR));
        const char *s = fn ? fn(resource) : NULL;
        if (s) {
            strncpy_s(out, out_size, s, _TRUNCATE);
        }
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        out[0] = 0;
    }
}

static int ends_with_mgrp(const char *s) {
    if (!s) {
        return 0;
    }
    size_t n = strlen(s);
    return n >= 5 && _stricmp(s + n - 5, ".mgrp") == 0;
}

static void sanitize(char *s) {
    if (!s) {
        return;
    }
    for (; *s; ++s) {
        unsigned char c = (unsigned char)*s;
        if (c <= 32 || *s == ':' || *s == '\\' || *s == '/' || *s == '*' || *s == '?' ||
            *s == '"' || *s == '<' || *s == '>' || *s == '|') {
            *s = '_';
        }
    }
}

static void dump_job(const CaptureJob *job) {
    if (!job || !job->buffer || job->size <= 16 || job->size > 64 * 1024 * 1024) {
        return;
    }

    char safe_path[260];
    strncpy_s(safe_path, sizeof(safe_path), job->path[0] ? job->path : "unknown", _TRUNCATE);
    sanitize(safe_path);

    char out_path[MAX_PATH];
    _snprintf_s(out_path, sizeof(out_path), _TRUNCATE,
                "%s\\passive_%06u_delay%04lu_res%08X_buf%08X_size%u_%s.bin",
                OUT_DIR, job->serial, (unsigned long)job->delay_ms,
                (unsigned)job->resource, (unsigned)(uintptr_t)job->buffer,
                job->size, safe_path);

    HANDLE h = CreateFileA(out_path, GENERIC_WRITE, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) {
        log_line("DUMP_FAIL serial=%u delay=%lu resource=%d buf=%p size=%u path=%s err=%lu",
                 job->serial, (unsigned long)job->delay_ms, job->resource, job->buffer,
                 job->size, job->path, GetLastError());
        return;
    }

    DWORD written = 0;
    BOOL ok = FALSE;
    __try {
        ok = WriteFile(h, job->buffer, job->size, &written, NULL);
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        ok = FALSE;
    }
    CloseHandle(h);

    log_line("DUMP serial=%u delay=%lu ok=%d written=%lu resource=%d buf=%p size=%u path=%s file=%s",
             job->serial, (unsigned long)job->delay_ms, ok ? 1 : 0,
             (unsigned long)written, job->resource, job->buffer, job->size,
             job->path, out_path);
}

static DWORD WINAPI DelayedDumpThread(void *param) {
    CaptureJob *job = (CaptureJob *)param;
    if (!job) {
        return 1;
    }
    Sleep(job->delay_ms);
    dump_job(job);
    HeapFree(GetProcessHeap(), 0, job);
    return 0;
}

static void queue_dump(unsigned serial, int resource, void *buffer, unsigned size, const char *path, DWORD delay_ms) {
    CaptureJob *job = (CaptureJob *)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(CaptureJob));
    if (!job) {
        return;
    }
    job->serial = serial;
    job->resource = resource;
    job->buffer = buffer;
    job->size = size;
    job->delay_ms = delay_ms;
    strncpy_s(job->path, sizeof(job->path), path ? path : "", _TRUNCATE);
    HANDLE thread = CreateThread(NULL, 0, DelayedDumpThread, job, 0, NULL);
    if (thread) {
        CloseHandle(thread);
    } else {
        HeapFree(GetProcessHeap(), 0, job);
    }
}

static int __cdecl HookRomRead(int resource, void *buffer, void *callback, void *context) {
    unsigned serial = (unsigned)InterlockedIncrement(&g_serial);
    int size = safe_size_for_resource(resource);
    char path[260];
    safe_path_for_resource(resource, path, sizeof(path));
    int capture = buffer && size > 16 && ends_with_mgrp(path);

    log_line("ROMREAD serial=%u resource=%d buf=%p size=%d cb=%p ctx=%p capture=%d path=%s",
             serial, resource, buffer, size, callback, context, capture, path);

    int rc = g_original_romread(resource, buffer, callback, context);

    if (capture) {
        CaptureJob immediate;
        ZeroMemory(&immediate, sizeof(immediate));
        immediate.serial = serial;
        immediate.resource = resource;
        immediate.buffer = buffer;
        immediate.size = (unsigned)size;
        immediate.delay_ms = 0;
        strncpy_s(immediate.path, sizeof(immediate.path), path, _TRUNCATE);
        dump_job(&immediate);
        queue_dump(serial, resource, buffer, (unsigned)size, path, 50);
        queue_dump(serial, resource, buffer, (unsigned)size, path, 250);
        queue_dump(serial, resource, buffer, (unsigned)size, path, 1000);
    }

    return rc;
}

static DWORD WINAPI InstallThread(void *) {
    ensure_dirs();
    Sleep(1000);

    HMODULE ffx = GetModuleHandleA("FFX.exe");
    if (!ffx) {
        log_line("INSTALL_FAIL no FFX.exe module");
        return 1;
    }
    g_ffx_base = (uintptr_t)ffx;

    uintptr_t ptr_addr = g_ffx_base + rva(VA_ROMREAD_PTR);
    RomReadFn current = NULL;
    __try {
        current = *(RomReadFn *)ptr_addr;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        current = NULL;
    }

    if (!current) {
        log_line("INSTALL_FAIL null romread ptr addr=%p base=%p", (void *)ptr_addr, (void *)g_ffx_base);
        return 2;
    }
    if ((void *)current == (void *)&HookRomRead) {
        log_line("INSTALL_SKIP already hooked base=%p ptr=%p", (void *)g_ffx_base, (void *)ptr_addr);
        return 0;
    }

    DWORD old_protect = 0;
    if (!VirtualProtect((void *)ptr_addr, sizeof(void *), PAGE_READWRITE, &old_protect)) {
        log_line("INSTALL_FAIL VirtualProtect addr=%p err=%lu", (void *)ptr_addr, GetLastError());
        return 3;
    }

    g_original_romread = current;
    *(RomReadFn *)ptr_addr = &HookRomRead;
    DWORD ignored = 0;
    VirtualProtect((void *)ptr_addr, sizeof(void *), old_protect, &ignored);
    FlushInstructionCache(GetCurrentProcess(), (void *)ptr_addr, sizeof(void *));

    log_line("INSTALL_OK passive base=%p ptr_addr=%p original=%p hook=%p",
             (void *)g_ffx_base, (void *)ptr_addr, (void *)g_original_romread, (void *)&HookRomRead);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hinst);
        CreateThread(NULL, 0, InstallThread, NULL, 0, NULL);
    }
    return TRUE;
}
