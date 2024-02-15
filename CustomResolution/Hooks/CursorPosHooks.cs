using CustomResolution.WndProcHookManagerProxyApi;
using Dalamud.Hooking;
using System;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;


namespace CustomResolution.Hooks;

// THIS. IS. UGLY.
public sealed unsafe class CursorPosHooks : IDisposable
{
    private readonly Hook<GetCursorPosDelegate> _getCursorPosHook;
    private readonly Hook<SetCursorPosDelegate> _setCursorPosHook;

    public CursorPosHooks()
    {
        _getCursorPosHook = Service.GameInteropProvider.HookFromSymbol<GetCursorPosDelegate>("user32.dll", "GetCursorPos", GetCursorPosDetour);
        _getCursorPosHook.Enable();

        _setCursorPosHook = Service.GameInteropProvider.HookFromSymbol<SetCursorPosDelegate>("user32.dll", "SetCursorPos", SetCursorPosDetour);
        _setCursorPosHook.Enable();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool GetCursorPosDelegate(POINT* lpPoint);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private unsafe delegate bool SetCursorPosDelegate(int x, int y);

    public void Dispose()
    {
        _getCursorPosHook.Dispose();
    }

    private bool GetCursorPosDetour(POINT* lpPoint)
    {
        var rv = _getCursorPosHook.Original(lpPoint);

        if (!rv)
        {
            return false;
        }

        Service.Plugin.ConvertCoordsWinToGame(ref lpPoint->x, ref lpPoint->y);

        return rv;
    }

    private bool SetCursorPosDetour(int x, int y)
    {
        Service.Plugin.ConvertCoordsGameToWin(ref x, ref y);

        return _setCursorPosHook.Original(x, y);
    }
}
