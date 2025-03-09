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
        _setCursorPosHook.Dispose();
    }

    private bool GetCursorPosDetour(POINT* lpPoint)
    {
        var rv = _getCursorPosHook.Original(lpPoint);

        if (!rv)
        {
            return false;
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

}
