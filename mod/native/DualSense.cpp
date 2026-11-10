// DualSense support for CupheadVR. A PlayStation 5 DualSense (or DualSense Edge) is read directly over HID, USB or
// Bluetooth, and handed to the game as an Xbox pad: XInputGetState is hooked (MinHook, BSD-2-Clause) and, while no real
// Xbox pad is connected, the first empty XInput slot reports the DualSense mapped to Xbox buttons. The game (Rewired)
// and the mod menu both read XInput, so both get it with the Xbox layout Steve's mods use. Report layouts follow the
// public DualSense documentation (USB report 0x01, Bluetooth report 0x31; Bluetooth switches to the full report after
// feature report 0x05 is read). No rumble or lights.
#include <windows.h>
#include <hidsdi.h>
#include <setupapi.h>
#include <xinput.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>
#include "MinHook.h"

#define API extern "C" __declspec(dllexport)

void NativeLog(const char* fmt, ...);  // CupheadVRNative.cpp

namespace {

struct PadState {
    unsigned short buttons;  // XInput button bits
    unsigned char lt, rt;
    short lx, ly, rx, ry;
};

std::atomic<int> g_connected{0};        // 1 USB, 2 Bluetooth, 0 none
std::atomic<int> g_injectSlot{-1};      // XInput slot the DualSense appears in, -1 none
std::atomic<unsigned> g_packet{0};
std::atomic<bool> g_enabled{true};
std::atomic<bool> g_running{false};
std::atomic<bool> g_hooked{false};
SRWLOCK g_lock = SRWLOCK_INIT;
PadState g_state{};

typedef DWORD(WINAPI* XInputGetStateFn)(DWORD, XINPUT_STATE*);
XInputGetStateFn g_realGetState = nullptr;
XInputGetStateFn g_realGetStateEx = nullptr;

short Axis(unsigned char v, bool invert) {
    int c = (int)v - 128;
    if (invert) c = -c - 1;
    int s = c * 258;
    if (s > 32767) s = 32767;
    if (s < -32768) s = -32768;
    return (short)s;
}

// d points at the left stick X byte. USB: report + 1. Bluetooth full report 0x31: report + 2.
void ParseFull(const unsigned char* d) {
    PadState p{};
    p.lx = Axis(d[0], false); p.ly = Axis(d[1], true);
    p.rx = Axis(d[2], false); p.ry = Axis(d[3], true);
    p.lt = d[4]; p.rt = d[5];
    unsigned char b0 = d[7], b1 = d[8], b2 = d[9];
    static const unsigned short hat[9] = {
        XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_RIGHT,
        XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_DOWN,
        XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_LEFT,
        XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_LEFT, 0};
    unsigned short b = hat[(b0 & 0x0f) < 9 ? (b0 & 0x0f) : 8];
    if (b0 & 0x10) b |= XINPUT_GAMEPAD_X;      // square
    if (b0 & 0x20) b |= XINPUT_GAMEPAD_A;      // cross
    if (b0 & 0x40) b |= XINPUT_GAMEPAD_B;      // circle
    if (b0 & 0x80) b |= XINPUT_GAMEPAD_Y;      // triangle
    if (b1 & 0x01) b |= XINPUT_GAMEPAD_LEFT_SHOULDER;
    if (b1 & 0x02) b |= XINPUT_GAMEPAD_RIGHT_SHOULDER;
    if (b1 & 0x10) b |= XINPUT_GAMEPAD_BACK;   // create
    if (b1 & 0x20) b |= XINPUT_GAMEPAD_START;  // options
    if (b1 & 0x40) b |= XINPUT_GAMEPAD_LEFT_THUMB;
    if (b1 & 0x80) b |= XINPUT_GAMEPAD_RIGHT_THUMB;
    if (b2 & 0x02) b |= XINPUT_GAMEPAD_BACK;   // touchpad click also works as Back
    p.buttons = b;
    AcquireSRWLockExclusive(&g_lock);
    g_state = p;
    ReleaseSRWLockExclusive(&g_lock);
    g_packet++;
}

// Bluetooth simple report 0x01 (before the switch to the full report): sticks, then buttons, then triggers.
void ParseSimpleBt(const unsigned char* r) {
    unsigned char full[10] = {r[1], r[2], r[3], r[4], r[8], r[9], 0, r[5], r[6], r[7]};
    ParseFull(full);
}

std::wstring FindPad(int* bluetooth) {
    GUID hid;
    HidD_GetHidGuid(&hid);
    HDEVINFO set = SetupDiGetClassDevsW(&hid, nullptr, nullptr, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (set == INVALID_HANDLE_VALUE) return L"";
    std::wstring found;
    SP_DEVICE_INTERFACE_DATA ifd{sizeof(ifd)};
    for (DWORD i = 0; found.empty() && SetupDiEnumDeviceInterfaces(set, nullptr, &hid, i, &ifd); i++) {
        DWORD size = 0;
        SetupDiGetDeviceInterfaceDetailW(set, &ifd, nullptr, 0, &size, nullptr);
        if (!size) continue;
        std::string buf(size, '\0');
        auto* det = (SP_DEVICE_INTERFACE_DETAIL_DATA_W*)&buf[0];
        det->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W);
        if (!SetupDiGetDeviceInterfaceDetailW(set, &ifd, det, size, nullptr, nullptr)) continue;
        HANDLE h = CreateFileW(det->DevicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (h == INVALID_HANDLE_VALUE) continue;
        HIDD_ATTRIBUTES a{sizeof(a)};
        if (HidD_GetAttributes(h, &a) && a.VendorID == 0x054C && (a.ProductID == 0x0CE6 || a.ProductID == 0x0DF2)) {
            PHIDP_PREPARSED_DATA pp = nullptr;
            HIDP_CAPS caps{};
            if (HidD_GetPreparsedData(h, &pp)) {
                HidP_GetCaps(pp, &caps);
                HidD_FreePreparsedData(pp);
            }
            // The gamepad collection: usage page 1, usage 5. USB input reports are 64 bytes, Bluetooth 78.
            if (caps.UsagePage == 1 && caps.Usage == 5) {
                found = det->DevicePath;
                *bluetooth = caps.InputReportByteLength != 64;
                NativeLog("DualSense found: product %04x, %s, input report %u bytes", a.ProductID, *bluetooth ? "Bluetooth" : "USB", caps.InputReportByteLength);
            }
        }
        CloseHandle(h);
    }
    SetupDiDestroyDeviceInfoList(set);
    return found;
}

void ReadLoop() {
    int lastLogged = -1;
    while (g_running) {
        int bt = 0;
        std::wstring path = g_enabled ? FindPad(&bt) : L"";
        if (path.empty()) {
            g_connected = 0;
            if (lastLogged != 0) { if (lastLogged > 0) NativeLog("DualSense disconnected"); lastLogged = 0; }
            for (int i = 0; i < 20 && g_running; i++) Sleep(100);
            continue;
        }
        HANDLE h = CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (h == INVALID_HANDLE_VALUE) h = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
        if (h == INVALID_HANDLE_VALUE) { NativeLog("DualSense could not be opened (%lu)", GetLastError()); Sleep(2000); continue; }
        if (bt) {
            unsigned char feat[64] = {0x05};
            if (HidD_GetFeature(h, feat, 41)) NativeLog("DualSense Bluetooth: asked for the full report");
        }
        g_connected = bt ? 2 : 1;
        lastLogged = g_connected;
        OVERLAPPED ov{};
        ov.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        unsigned char rep[128];
        int idle = 0;
        while (g_running && g_enabled) {
            DWORD got = 0;
            ResetEvent(ov.hEvent);
            if (!ReadFile(h, rep, sizeof(rep), nullptr, &ov) && GetLastError() != ERROR_IO_PENDING) break;
            DWORD w = WaitForSingleObject(ov.hEvent, 500);
            if (w != WAIT_OBJECT_0) {
                CancelIo(h);
                if (++idle > 20) break;  // nothing for 10 s: look again
                continue;
            }
            if (!GetOverlappedResult(h, &ov, &got, FALSE)) break;
            idle = 0;
            if (got >= 64 && rep[0] == 0x01) ParseFull(rep + 1);
            else if (got >= 12 && rep[0] == 0x31) ParseFull(rep + 2);
            else if (got >= 10 && rep[0] == 0x01) ParseSimpleBt(rep);
        }
        CancelIo(h);
        CloseHandle(ov.hEvent);
        CloseHandle(h);
        g_connected = 0;
        AcquireSRWLockExclusive(&g_lock);
        g_state = PadState{};
        ReleaseSRWLockExclusive(&g_lock);
    }
}

// Which slot to use: the first empty one, but only while no real Xbox pad (or Steam Input's virtual pad) is connected,
// so a pad Steam already translates is never doubled.
DWORD WINAPI HookCommon(XInputGetStateFn real, DWORD index, XINPUT_STATE* out) {
    DWORD r = real(index, out);
    if (!g_enabled || g_connected == 0) { if (g_injectSlot >= 0) g_injectSlot = -1; return r; }
    if (r == ERROR_SUCCESS) {
        if ((int)index == g_injectSlot) g_injectSlot = -1;  // a real pad took the slot
        return r;
    }
    int slot = g_injectSlot;
    if (slot < 0) {
        // Polling empty slots is slow, so look for a free slot at most once a second.
        static std::atomic<ULONGLONG> lastLook{0};
        ULONGLONG now = GetTickCount64();
        if (now - lastLook < 1000) return r;
        lastLook = now;
        XINPUT_STATE tmp;
        for (DWORD i = 0; i < 4; i++) if (real(i, &tmp) == ERROR_SUCCESS) return r;  // a real pad is present
        for (DWORD i = 0; i < 4; i++) if (real(i, &tmp) != ERROR_SUCCESS) { slot = (int)i; break; }
        g_injectSlot = slot;
        NativeLog("DualSense shown to the game as Xbox pad %d", slot);
    }
    if ((int)index != slot || !out) return r;
    AcquireSRWLockShared(&g_lock);
    PadState p = g_state;
    ReleaseSRWLockShared(&g_lock);
    memset(out, 0, sizeof(*out));
    out->dwPacketNumber = g_packet;
    out->Gamepad.wButtons = p.buttons;
    out->Gamepad.bLeftTrigger = p.lt;
    out->Gamepad.bRightTrigger = p.rt;
    out->Gamepad.sThumbLX = p.lx; out->Gamepad.sThumbLY = p.ly;
    out->Gamepad.sThumbRX = p.rx; out->Gamepad.sThumbRY = p.ry;
    return ERROR_SUCCESS;
}

DWORD WINAPI HookGetState(DWORD index, XINPUT_STATE* out) { return HookCommon(g_realGetState, index, out); }
DWORD WINAPI HookGetStateEx(DWORD index, XINPUT_STATE* out) { return HookCommon(g_realGetStateEx, index, out); }

}  // namespace

// Starts the reader and hooks XInput. Safe to call more than once.
API int CupPad_Start() {
    if (g_running) return 1;
    if (!g_hooked) {
        HMODULE x = LoadLibraryW(L"xinput1_4.dll");
        if (!x) { NativeLog("DualSense off: no xinput1_4.dll"); return 0; }
        MH_STATUS s = MH_Initialize();
        if (s != MH_OK && s != MH_ERROR_ALREADY_INITIALIZED) { NativeLog("DualSense off: MinHook %d", (int)s); return 0; }
        void* gs = (void*)GetProcAddress(x, "XInputGetState");
        void* gsx = (void*)GetProcAddress(x, (LPCSTR)100);  // XInputGetStateEx, also reports the guide button
        bool ok = gs && MH_CreateHook(gs, (void*)&HookGetState, (void**)&g_realGetState) == MH_OK && MH_EnableHook(gs) == MH_OK;
        if (gsx && MH_CreateHook(gsx, (void*)&HookGetStateEx, (void**)&g_realGetStateEx) == MH_OK) MH_EnableHook(gsx);
        if (!ok) { NativeLog("DualSense off: XInput hook failed"); return 0; }
        g_hooked = true;
    }
    g_running = true;
    std::thread(ReadLoop).detach();
    NativeLog("DualSense support on: looking for a pad");
    return 1;
}

API void CupPad_Enable(int on) { g_enabled = on != 0; }
// 0 none, 1 USB, 2 Bluetooth
API int CupPad_Connected() { return g_connected; }
// The XInput slot the DualSense is shown in, -1 when it is not shown (no pad, or a real Xbox pad is connected).
API int CupPad_Slot() { return g_injectSlot; }
