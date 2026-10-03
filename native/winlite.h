#pragma once
/* Minimal public Win32 declarations, avoiding a build-time VC runtime dependency. */
typedef unsigned char BYTE;
typedef unsigned short WCHAR;
typedef unsigned int uint32_t;
typedef unsigned long DWORD;
typedef int BOOL;
typedef void *HANDLE;
typedef void *HMODULE;
typedef void *HINSTANCE;
typedef void *LPVOID;
typedef const WCHAR *LPCWSTR;
typedef long long (__stdcall *FARPROC)(void);
#define WINAPI __stdcall
#define TRUE 1
#define FALSE 0
#define NULL ((void *)0)
#define DLL_PROCESS_ATTACH 1
#define FILE_APPEND_DATA 4
#define FILE_SHARE_READ 1
#define FILE_SHARE_WRITE 2
#define OPEN_ALWAYS 4
#define FILE_ATTRIBUTE_NORMAL 128
#define INVALID_HANDLE_VALUE ((HANDLE)-1)
#define GET_MODULE_HANDLE_EX_FLAG_PIN 1
#define GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS 4
#define API __declspec(dllimport)
API DWORD WINAPI GetModuleFileNameW(HMODULE, WCHAR *, DWORD);
API BOOL WINAPI GetModuleHandleExW(DWORD, LPCWSTR, HMODULE *);
API HMODULE WINAPI GetModuleHandleW(LPCWSTR);
API FARPROC WINAPI GetProcAddress(HMODULE, const char *);
API HANDLE WINAPI CreateFileW(LPCWSTR, DWORD, DWORD, LPVOID, DWORD, DWORD, HANDLE);
API BOOL WINAPI WriteFile(HANDLE, const void *, DWORD, DWORD *, LPVOID);
API BOOL WINAPI CloseHandle(HANDLE);
API HANDLE WINAPI CreateThread(LPVOID, unsigned long long, DWORD (WINAPI *)(LPVOID), LPVOID, DWORD, DWORD *);
API void WINAPI Sleep(DWORD);
API int WINAPI lstrcmpiW(LPCWSTR, LPCWSTR);
