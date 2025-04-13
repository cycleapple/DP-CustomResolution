using Dalamud.Hooking;
using System;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;

namespace CustomResolution.Hooks;

// THIS. IS. UGLY.
public sealed unsafe class CursorPosHooks : IDisposable
{
    private readonly Hook<GetCursorPosDelegate> _getCursorPosHook;
    private readonly Hook<SetCursorPosDelegate> _setCursorPosHook;
    private readonly Hook<ScreenToClientDelegate> _screenToClientHook;
    private readonly Hook<ClientToScreenDelegate> _clientToScreenHook;

    public CursorPosHooks()
    {
        _getCursorPosHook = Service.GameInteropProvider.HookFromSymbol<GetCursorPosDelegate>("user32.dll", "GetCursorPos", GetCursorPosDetour);
        _getCursorPosHook.Enable();

        _setCursorPosHook = Service.GameInteropProvider.HookFromSymbol<SetCursorPosDelegate>("user32.dll", "SetCursorPos", SetCursorPosDetour);
        _setCursorPosHook.Enable();

        _screenToClientHook = Service.GameInteropProvider.HookFromSymbol<ScreenToClientDelegate>("user32.dll", "ScreenToClient", ScreenToClientDetour);
        _screenToClientHook.Enable();

        _clientToScreenHook = Service.GameInteropProvider.HookFromSymbol<ClientToScreenDelegate>("user32.dll", "ClientToScreen", ClientToScreenDetour);
        _clientToScreenHook.Enable();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool GetCursorPosDelegate(POINT* lpPoint);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool SetCursorPosDelegate(int x, int y);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool ScreenToClientDelegate(HWND hWnd, POINT* lpPoint);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool ClientToScreenDelegate(HWND hWnd, POINT* lpPoint);

    public void Dispose()
    {
        _getCursorPosHook.Dispose();
        _setCursorPosHook.Dispose();
        _screenToClientHook.Dispose();
        _clientToScreenHook.Dispose();
    }

    public bool GetCursorPosOrig(POINT* lpPoint)
    {
        return _getCursorPosHook.Original(lpPoint);
    }

    public bool SetCursorPosOrig(int x, int y)
    {
        return _setCursorPosHook.Original(x, y);
    }

    public bool ScreenToClientOrig(HWND hWnd, POINT* lpPoint)
    {
        return _screenToClientHook.Original(hWnd, lpPoint);
    }

    public bool ClientToScreenOrig(HWND hWnd, POINT* lpPoint)
    {
        return _clientToScreenHook.Original(hWnd, lpPoint);
    }

    private bool GetCursorPosDetour(POINT* lpPoint)
    {
        var rv = _getCursorPosHook.Original(lpPoint);
        if (!rv || lpPoint == null)
        {
            return rv;
        }

#if false
        Service.PluginLog.Debug($"GetCursorPos A @ {lpPoint->x} {lpPoint -> y}");
#endif

        Service.Plugin.ConvertCoordsGlobalToGame(ref lpPoint->x, ref lpPoint->y);

#if false
        Service.PluginLog.Debug($"GetCursorPos B @ {lpPoint->x} {lpPoint->y}");
#endif

        return rv;
    }

    private bool SetCursorPosDetour(int x, int y)
    {
#if false
        Service.PluginLog.Debug($"SetCursorPos A @ {x} {y}");
#endif

        Service.Plugin.ConvertCoordsGameToGlobal(ref x, ref y);

#if false
        Service.PluginLog.Debug($"SetCursorPos B @ {x} {y}");
#endif

        return _setCursorPosHook.Original(x, y);
    }

    private bool ScreenToClientDetour(HWND hWnd, POINT* lpPoint)
    {
        return _screenToClientHook.Original(hWnd, lpPoint);
    }

    private bool ClientToScreenDetour(HWND hWnd, POINT* lpPoint)
    {
        return _clientToScreenHook.Original(hWnd, lpPoint);
    }
}
