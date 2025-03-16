using CustomResolution.WndProcHookManagerProxyApi;
using Dalamud.Hooking;
using Serilog.Events;
using System;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;


namespace CustomResolution.Hooks;

// THIS. IS. UGLY.
public sealed unsafe class WindowRectHooks : IDisposable
{
    private readonly Hook<GetClientRectDelegate> _getClientRectHook;
    private readonly Hook<SetWindowPosDelegate> _setWindowPosHook;

    public WindowRectHooks()
    {
        _getClientRectHook = Service.GameInteropProvider.HookFromSymbol<GetClientRectDelegate>("user32.dll", "GetClientRect", GetClientRectDetour);
        _getClientRectHook.Enable();

        _setWindowPosHook = Service.GameInteropProvider.HookFromSymbol<SetWindowPosDelegate>("user32.dll", "SetWindowPos", SetWindowPosDetour);
        _setWindowPosHook.Enable();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool GetClientRectDelegate(HWND hWnd, RECT* lpRect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool SetWindowPosDelegate(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public void Dispose()
    {
        _getClientRectHook.Dispose();
        _setWindowPosHook.Dispose();
    }

    public bool GetClientRectOrig(HWND hWnd, RECT* lpRect)
    {
        return _getClientRectHook.Original(hWnd, lpRect);
    }

    private bool GetClientRectDetour(HWND hWnd, RECT* lpRect)
    {
        var rv = _getClientRectHook.Original(hWnd, lpRect);

        if (!rv)
        {
            return false;
        }

#if false
        Service.PluginLog.Debug($"GetClientRectDetour A @ {lpRect->left} {lpRect->top} {lpRect->right} {lpRect->bottom}");
#endif

        Service.Plugin.ConvertCoordsWinToGame(ref lpRect->right, ref lpRect->bottom);

#if false
        Service.PluginLog.Debug($"GetClientRectDetour B @ {lpRect->left} {lpRect->top} {lpRect->right} {lpRect->bottom}");
#endif

        return rv;
    }

    private bool SetWindowPosDetour(HWND hWnd, HWND hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
    {
#if false
        Service.PluginLog.Debug($"SetWindowPosDetour A @ {X} {Y} {cx} {cy}");
#endif

        Service.Plugin.ConvertCoordsGameToWin(ref cx, ref cy);

#if false
        Service.PluginLog.Debug($"SetWindowPosDetour B @ {X} {Y} {cx} {cy}");
#endif

        return _setWindowPosHook.Original(hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags);
    }

}
