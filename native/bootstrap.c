#include "winlite.h"

/* All addresses in this record are RVAs in the final original DLL. */
__declspec(dllexport) volatile struct {
    uint32_t magic, original_entry, managed_rva, managed_size;
} affine_boot_config = { 0x37464641, 0, 0, 0 };

typedef void *Ptr;
typedef struct {
    Ptr (__cdecl *root)(void);
    Ptr (__cdecl *attach)(Ptr);
    void (__cdecl *detach)(Ptr);
    void (__cdecl *domains)(void (__cdecl *)(Ptr, Ptr), Ptr);
    const char *(__cdecl *domain_name)(Ptr);
    int (__cdecl *domain_set)(Ptr, int);
    void (__cdecl *assemblies)(void (__cdecl *)(Ptr, Ptr), Ptr);
    Ptr (__cdecl *assembly_image)(Ptr);
    const char *(__cdecl *image_name)(Ptr);
    Ptr (__cdecl *image_open)(char *, uint32_t, int, int *, int);
    Ptr (__cdecl *assembly_load)(Ptr, const char *, int *, int);
    void (__cdecl *image_close)(Ptr);
    Ptr (__cdecl *class_from_name)(Ptr, const char *, const char *);
    Ptr (__cdecl *method_from_name)(Ptr, const char *, int);
    Ptr (__cdecl *invoke)(Ptr, Ptr, Ptr *, Ptr *);
} MonoApi;
static MonoApi api;
static int ready;
static Ptr child;


static int same(const char *a, const char *b) {
    if (!a || !b) return 0;
    while (*a && *a == *b) { ++a; ++b; }
    return *a == *b;
}
static void log_line(HMODULE self, const char *text) {
    WCHAR path[1024]; DWORD n = GetModuleFileNameW(self, path, 980), bytes = 0;
    if (!n || n >= 980) return;
    while (n && path[n-1] != L'\\') --n;
    const WCHAR suffix[] = L"affine-single-native.log";
    for (unsigned i = 0; i < sizeof(suffix)/sizeof(WCHAR); ++i) path[n+i] = suffix[i];
    HANDLE f = CreateFileW(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    DWORD len = 0; while (text[len]) ++len;
    WriteFile(f, text, len, &bytes, NULL); WriteFile(f, "\r\n", 2, &bytes, NULL); CloseHandle(f);
}
static void __cdecl visit_domain(Ptr domain, Ptr unused) {
    (void)unused;
    if (same(api.domain_name(domain), "Unity Child Domain")) child = domain;
}
static void __cdecl visit_assembly(Ptr assembly, Ptr unused) {
    (void)unused;
    const char *name = api.image_name(api.assembly_image(assembly));
    if (same(name, "MelonLoader") || same(name, "MelonLoader.dll")) ready |= 1;
    if (same(name, "0Harmony") || same(name, "0Harmony.dll")) ready |= 2;
    if (same(name, "Assembly-CSharp") || same(name, "Assembly-CSharp.dll")) ready |= 4;
}
#define GET(field, name) do { *(FARPROC *)&api.field = GetProcAddress(mono, name); if (!api.field) { log_line(self, "ABORT missing Mono API: " name); return 0; } } while (0)
static DWORD WINAPI worker(LPVOID param) {
    HMODULE self = (HMODULE)param, pinned = NULL, mono = NULL;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN, (LPCWSTR)self, &pinned)) return 0;
    log_line(self, "START AffineSingle native bootstrap v8 in Sinmai");
    for (int i = 0; i < 600; ++i) {
        mono = GetModuleHandleW(L"mono-2.0-bdwgc.dll");
        if (mono) break;
        Sleep(250);
    }
    if (!mono) { log_line(self, "ABORT Unity Mono not loaded within 150 seconds"); return 0; }
    log_line(self, "STAGE Mono module found");
    GET(root, "mono_get_root_domain"); GET(attach, "mono_thread_attach"); GET(detach, "mono_thread_detach");
    GET(domains, "mono_domain_foreach"); GET(domain_name, "mono_domain_get_friendly_name"); GET(domain_set, "mono_domain_set");
    GET(assemblies, "mono_assembly_foreach"); GET(assembly_image, "mono_assembly_get_image"); GET(image_name, "mono_image_get_name");
    GET(image_open, "mono_image_open_from_data_full"); GET(assembly_load, "mono_assembly_load_from_full"); GET(image_close, "mono_image_close");
    GET(class_from_name, "mono_class_from_name"); GET(method_from_name, "mono_class_get_method_from_name"); GET(invoke, "mono_runtime_invoke");
    Ptr root = NULL, thread = NULL;
    for (int i = 0; i < 600; ++i) { root = api.root(); if (root) break; Sleep(250); }
    if (!root) { log_line(self, "ABORT Mono root domain timeout"); return 0; }
    log_line(self, "STAGE Mono root found");
    Sleep(500);
    thread = api.attach(root);
    if (!thread) { log_line(self, "ABORT Mono thread attach failed"); return 0; }
    log_line(self, "STAGE Mono thread attached");
    for (int i = 0; i < 600; ++i) {
        ready = 0; child = NULL;
        api.domains(visit_domain, NULL);
        /* Unity 2018 may keep game and Melon assemblies in the root domain. */
        if (!child) child = root;
        if (api.domain_set(child, 0)) api.assemblies(visit_assembly, NULL);
        if (ready == 7 && child) break;
        Sleep(250);
    }
    if (ready != 7 || !child) { log_line(self, "ABORT game domain / MelonLoader / Harmony / Assembly-CSharp not ready"); api.detach(thread); return 0; }
    int status = 0;
    Ptr img = api.image_open((char *)self + affine_boot_config.managed_rva, affine_boot_config.managed_size, 1, &status, 0);
    if (!img) { log_line(self, "ABORT embedded managed image invalid"); api.detach(thread); return 0; }
    Ptr assembly = api.assembly_load(img, "AffineSingle.Managed.dll", &status, 0);
    if (!assembly) { log_line(self, "ABORT embedded assembly load failed"); api.image_close(img); api.detach(thread); return 0; }
    Ptr klass = api.class_from_name(api.assembly_image(assembly), "AffineGameLights", "NativeEntry");
    Ptr method = klass ? api.method_from_name(klass, "Start", 1) : NULL;
    if (!method) { log_line(self, "ABORT managed Start missing"); api.image_close(img); api.detach(thread); return 0; }
    Ptr module = self, args[1] = { &module }, exception = NULL;
    api.invoke(method, NULL, args, &exception);
    log_line(self, exception ? "ABORT managed Start threw; check MelonLoader log" : "QUEUED managed bridge initialization on game thread");
    api.image_close(img); api.detach(thread);
    return 0;
}
BOOL WINAPI affine_entry(HINSTANCE self, DWORD reason, LPVOID reserved) {
    typedef BOOL (WINAPI *DllEntry)(HINSTANCE, DWORD, LPVOID);
    if (affine_boot_config.original_entry) {
        DllEntry original = (DllEntry)((BYTE *)self + affine_boot_config.original_entry);
        if (!original(self, reason, reserved)) return FALSE;
    }
    if (reason == DLL_PROCESS_ATTACH) {
        WCHAR exe[1024]; DWORD n = GetModuleFileNameW(NULL, exe, 1024);
        if (n && n < 1024) {
            WCHAR *name = exe + n; while (name > exe && name[-1] != L'\\') --name;
            if (lstrcmpiW(name, L"Sinmai.exe") == 0) {
                HANDLE t = CreateThread(NULL, 0, worker, self, 0, NULL);
                if (t) CloseHandle(t);
            }
        }
    }
    return TRUE;
}
