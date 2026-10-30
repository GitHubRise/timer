// GameClockTray.dll - draws the GameClock time inside the Windows taskbar clock.
//
// This DLL is loaded into explorer.exe by the GameClock client. It subclasses the
// taskbar clock window (TrayClockWClass), replaces its painting with a two line
// text drawn with GDI, and restores the original window procedure when asked to.
//
// The client talks to the DLL through messages sent to the clock window:
//   WM_COPYDATA (dwData = GCT_CONFIG_MAGIC)  -> replace the configuration
//   WM_APP + 1                               -> uninstall and restore the clock
//   WM_APP + 2                               -> query: 1 = installed, 0 = not
//   WM_APP + 3                               -> reinstall on the current clock
//
// The DLL has no exported functions: loading it starts a worker thread that
// finds the clock window and installs the subclass.

#include <windows.h>
#include <uxtheme.h>

#define GCT_KEY_COLOR RGB(255, 0, 255)

#define GCT_CONFIG_MAGIC 0x31544347u // 'G' | 'C' << 8 | 'T' << 16 | '1' << 24
#define GCT_MSG_UNINSTALL (WM_APP + 1)
#define GCT_MSG_QUERY     (WM_APP + 2)
#define GCT_MSG_INSTALL   (WM_APP + 3)
#define GCT_TIMER_ID      0x4743
#define GCT_IDLE_TIMEOUT  15000

#pragma pack(push, 4)
struct GCTCONFIG
{
    DWORD magic;
    DWORD flags;   // bit 0: bold, bit 1: italic
    DWORD color;   // COLORREF
    DWORD fontHeight;
    WCHAR fontFace[64];
    WCHAR line1[128];
    WCHAR line2[128];
};
#pragma pack(pop)

static WNDPROC g_originalProc = NULL;
static HWND g_clock = NULL;
static RECT g_originalRect = { 0, 0, 0, 0 };
static int g_originalWidth = 0;
static bool g_installed = false;
static bool g_hasConfig = false;
static DWORD g_lastConfigTick = 0;
static GCTCONFIG g_config;
static CRITICAL_SECTION g_lock;

static void CopyString(WCHAR* target, size_t capacity, const WCHAR* source)
{
    if (capacity == 0)
    {
        return;
    }
    size_t i = 0;
    if (source != NULL)
    {
        for (; i + 1 < capacity && source[i] != L'\0'; i++)
        {
            target[i] = source[i];
        }
    }
    target[i] = L'\0';
}

static HWND FindClockWindow()
{
    HWND shell = FindWindowW(L"Shell_TrayWnd", NULL);
    if (shell == NULL)
    {
        return NULL;
    }

    HWND tray = FindWindowExW(shell, NULL, L"TrayNotifyWnd", NULL);
    if (tray != NULL)
    {
        HWND clock = FindWindowExW(tray, NULL, L"TrayClockWClass", NULL);
        if (clock != NULL)
        {
            return clock;
        }
    }

    return FindWindowExW(shell, NULL, L"TrayClockWClass", NULL);
}

static LOGFONTW g_logFont;
static bool g_hasLogFont = false;
static COLORREF g_textColor = RGB(255, 255, 255);
static COLORREF g_bgColor = RGB(32, 32, 32);
static LONG_PTR g_originalExStyle = 0;

static int ColorLuma(COLORREF color)
{
    return (GetRValue(color) * 299 + GetGValue(color) * 587 + GetBValue(color) * 114) / 1000;
}

static void CaptureNativeTheme(HWND clock)
{
    RECT rect;
    GetWindowRect(clock, &rect);

    HFONT font = (HFONT)SendMessageW(clock, WM_GETFONT, 0, 0);
    if (font != NULL && GetObjectW(font, sizeof(g_logFont), &g_logFont) == sizeof(g_logFont))
    {
        g_hasLogFont = true;
    }
    if (!g_hasLogFont)
    {
        NONCLIENTMETRICSW metrics;
        ZeroMemory(&metrics, sizeof(metrics));
        metrics.cbSize = sizeof(metrics);
        if (SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, sizeof(metrics), &metrics, 0))
        {
            g_logFont = metrics.lfMessageFont;
        }
        else
        {
            ZeroMemory(&g_logFont, sizeof(g_logFont));
            g_logFont.lfHeight = -12;
            g_logFont.lfWeight = FW_NORMAL;
            CopyString(g_logFont.lfFaceName, LF_FACESIZE, L"Segoe UI");
        }
        g_hasLogFont = true;
    }

    DWORD lightTheme = 0;
    DWORD lightThemeSize = sizeof(lightTheme);
    if (RegGetValueW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"SystemUsesLightTheme", RRF_RT_REG_DWORD, NULL,
        &lightTheme, &lightThemeSize) != ERROR_SUCCESS)
    {
        lightTheme = 0;
    }
    g_textColor = lightTheme != 0 ? RGB(0, 0, 0) : RGB(255, 255, 255);
    g_bgColor = lightTheme != 0 ? RGB(243, 243, 243) : RGB(32, 32, 32);

}

static HFONT BuildClockFont(HDC hdc, const RECT& rc, const GCTCONFIG& config, int* characterExtra, int* usedHeight)
{
    int availableWidth = (rc.right - rc.left) - 2;
    int availableHeight = (rc.bottom - rc.top) / 2;
    int height = g_hasLogFont ? abs(g_logFont.lfHeight) : 12;
    if (height > availableHeight)
    {
        height = availableHeight;
    }
    if (height < 8)
    {
        height = 8;
    }

    LOGFONTW baseFont;
    if (g_hasLogFont)
    {
        baseFont = g_logFont;
    }
    else
    {
        ZeroMemory(&baseFont, sizeof(baseFont));
        baseFont.lfWeight = FW_NORMAL;
        CopyString(baseFont.lfFaceName, LF_FACESIZE, L"Segoe UI");
    }

    size_t length1 = wcslen(config.line1);
    size_t length2 = wcslen(config.line2);
    int characters = (int)(length1 > length2 ? length1 : length2);
    if (characters < 1)
    {
        characters = 1;
    }

    for (;;)
    {
        LOGFONTW fontInfo = baseFont;
        fontInfo.lfHeight = -height;
        fontInfo.lfQuality = ANTIALIASED_QUALITY;
        HFONT font = CreateFontIndirectW(&fontInfo);
        HGDIOBJ oldFont = SelectObject(hdc, font);

        SIZE first = { 0, 0 };
        SIZE second = { 0, 0 };
        if (config.line1[0] != L'\0')
        {
            GetTextExtentPoint32W(hdc, config.line1, (int)length1, &first);
        }
        if (config.line2[0] != L'\0')
        {
            GetTextExtentPoint32W(hdc, config.line2, (int)length2, &second);
        }
        SelectObject(hdc, oldFont);

        int widest = first.cx > second.cx ? first.cx : second.cx;
        int extra = 0;
        if (widest > availableWidth)
        {
            extra = (availableWidth - widest) / characters;
            if (extra < -3)
            {
                extra = -3;
            }
        }

        if (widest + extra * characters <= availableWidth || height <= 8)
        {
            *characterExtra = extra;
            *usedHeight = height;
            return font;
        }

        DeleteObject(font);
        height--;
        if (height < 8)
        {
            height = 8;
        }
    }
}

static void PaintClock(HWND hwnd, HDC hdc)
{
    RECT rc;
    GetClientRect(hwnd, &rc);
    if (rc.right <= 0 || rc.bottom <= 0)
    {
        return;
    }

    EnterCriticalSection(&g_lock);
    GCTCONFIG config = g_config;
    bool hasConfig = g_hasConfig;
    LeaveCriticalSection(&g_lock);

    if (!hasConfig)
    {
        return;
    }

    HBRUSH background = CreateSolidBrush(g_bgColor);
    FillRect(hdc, &rc, background);
    DeleteObject(background);

    int characterExtra = 0;
    int usedHeight = 0;
    HFONT font = BuildClockFont(hdc, rc, config, &characterExtra, &usedHeight);
    HGDIOBJ oldFont = SelectObject(hdc, font);
    int oldMode = SetBkMode(hdc, TRANSPARENT);
    COLORREF oldColor = SetTextColor(hdc, g_textColor);
    int oldExtra = SetTextCharacterExtra(hdc, characterExtra);

    int middle = (rc.top + rc.bottom) / 2;
    RECT upper = rc;
    RECT lower = rc;
    upper.bottom = middle;
    lower.top = middle;

    DrawTextW(hdc, config.line1, -1, &upper,
        DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);
    DrawTextW(hdc, config.line2, -1, &lower,
        DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX);

    SetTextCharacterExtra(hdc, oldExtra);
    SetTextColor(hdc, oldColor);
    SetBkMode(hdc, oldMode);
    SelectObject(hdc, oldFont);
    DeleteObject(font);
}
static void RestoreClock()
{
    HWND clock = g_clock;
    if (clock == NULL)
    {
        g_installed = false;
        return;
    }

    if (g_installed && g_originalProc != NULL)
    {
        SetWindowLongPtrW(clock, GWLP_WNDPROC, (LONG_PTR)g_originalProc);
    }

    KillTimer(clock, GCT_TIMER_ID);


    if (g_installed && g_originalWidth > 0)
    {
        HWND parent = GetParent(clock);
        POINT points[2];
        points[0].x = g_originalRect.left;
        points[0].y = g_originalRect.top;
        points[1].x = g_originalRect.right;
        points[1].y = g_originalRect.bottom;
        if (parent != NULL)
        {
            ScreenToClient(parent, &points[0]);
            ScreenToClient(parent, &points[1]);
        }
        SetWindowPos(clock, HWND_TOP, points[0].x, points[0].y,
            points[1].x - points[0].x, points[1].y - points[0].y,
            SWP_NOACTIVATE | SWP_NOZORDER);
    }

    g_installed = false;
    g_originalProc = NULL;
    InvalidateRect(clock, NULL, TRUE);
}

static LRESULT CALLBACK ClockProc(HWND hwnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_PAINT:
    case WM_PRINTCLIENT:
    {
        EnterCriticalSection(&g_lock);
        bool hasConfig = g_hasConfig;
        LeaveCriticalSection(&g_lock);
        if (!hasConfig)
        {
            break;
        }

        PAINTSTRUCT paint;
        HDC hdc;
        if (message == WM_PAINT)
        {
            hdc = BeginPaint(hwnd, &paint);
        }
        else
        {
            hdc = (HDC)wParam;
        }

        PaintClock(hwnd, hdc);

        if (message == WM_PAINT)
        {
            EndPaint(hwnd, &paint);
        }
        return 0;
    }

    case WM_ERASEBKGND:
        if (g_hasConfig)
        {
            return 1;
        }
        break;

    case WM_COPYDATA:
    {
        COPYDATASTRUCT* data = (COPYDATASTRUCT*)lParam;
        if (data == NULL || data->dwData != GCT_CONFIG_MAGIC ||
            data->lpData == NULL || data->cbData < (int)sizeof(GCTCONFIG))
        {
            break;
        }

        GCTCONFIG* config = (GCTCONFIG*)data->lpData;
        if (config->magic != GCT_CONFIG_MAGIC)
        {
            break;
        }

        EnterCriticalSection(&g_lock);
        CopyMemory(&g_config, config, sizeof(GCTCONFIG));
        g_hasConfig = true;
        g_lastConfigTick = GetTickCount();
        LeaveCriticalSection(&g_lock);

        SetTimer(hwnd, GCT_TIMER_ID, 1000, NULL);
        InvalidateRect(hwnd, NULL, TRUE);
        return TRUE;
    }

    case WM_TIMER:
        if (wParam == GCT_TIMER_ID)
        {
            if (GetTickCount() - g_lastConfigTick > GCT_IDLE_TIMEOUT)
            {
                RestoreClock();
            }
            else
            {
                InvalidateRect(hwnd, NULL, FALSE);
            }
            return 0;
        }
        break;

    case GCT_MSG_UNINSTALL:
        RestoreClock();
        return 0;

    case GCT_MSG_QUERY:
        return g_installed ? 1 : 0;

    case GCT_MSG_INSTALL:
        return g_installed ? 1 : 0;

    case WM_NCDESTROY:
        if (hwnd == g_clock)
        {
            g_clock = NULL;
            g_installed = false;
            g_originalProc = NULL;
        }
        break;

    default:
        break;
    }

    if (g_originalProc != NULL)
    {
        return CallWindowProcW(g_originalProc, hwnd, message, wParam, lParam);
    }
    return DefWindowProcW(hwnd, message, wParam, lParam);
}

static bool InstallOnClock(HWND clock)
{
    if (clock == NULL || !IsWindow(clock))
    {
        return false;
    }
    if (g_installed && g_clock == clock)
    {
        return true;
    }
    if (g_installed)
    {
        RestoreClock();
    }

    GetWindowRect(clock, &g_originalRect);
    g_originalWidth = g_originalRect.right - g_originalRect.left;
    CaptureNativeTheme(clock);

    SetLastError(0);
    WNDPROC previous = (WNDPROC)SetWindowLongPtrW(clock, GWLP_WNDPROC, (LONG_PTR)ClockProc);
    if (previous == NULL && GetLastError() != 0)
    {
        return false;
    }

    g_originalProc = previous;
    g_clock = clock;
    g_installed = true;
    SetTimer(clock, GCT_TIMER_ID, 1000, NULL);
    InvalidateRect(clock, NULL, TRUE);
    return true;
}

static DWORD WINAPI InstallThread(LPVOID)
{
    for (int i = 0; i < 120; i++)
    {
        HWND clock = FindClockWindow();
        if (clock != NULL && InstallOnClock(clock))
        {
            return 0;
        }
        Sleep(500);
    }
    return 0;
}
BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        InitializeCriticalSection(&g_lock);
        ZeroMemory(&g_config, sizeof(g_config));
        g_config.magic = GCT_CONFIG_MAGIC;
        g_config.fontHeight = 12;

        HANDLE thread = CreateThread(NULL, 0, InstallThread, NULL, 0, NULL);
        if (thread != NULL)
        {
            CloseHandle(thread);
        }
    }
    return TRUE;
}
