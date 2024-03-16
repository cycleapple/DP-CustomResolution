using CustomResolution.WndProcHookManagerProxyApi;
using Serilog.Events;
using System;
using System.Linq;
using System.Reflection.Emit;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;


namespace CustomResolution.Hooks;

// THIS. IS. UGLY.
public sealed unsafe class WndProcHook : IDisposable
{
    private static WndProcHook? _instance;

    private readonly WndProcHookManager _manager;

    private bool _applied = false;
    private DynamicMethod? _genMethod;
    private Delegate? _genDelegate;

    public WndProcHook()
    {
        _instance = this;

        _manager = new();

        Apply();
    }

    public void Dispose()
    {
        if (!_applied)
        {
            return;
        }

        if (_manager.PreWndProc is not { } preWndProcProp)
        {
            Service.PluginLog.Information("CustomResolution couldn't obtain the PreWndProc event.");

            return;
        }

        preWndProcProp.RemoveEventHandler(_manager.ProxiedValue, _genDelegate);

        _applied = false;
    }

    public static void InvokeStatic(object args)
    {
        if (_instance is { } instance)
        {
            instance.Invoke(new WndProcEventArgs(instance._manager, args));
        }
    }

    private static void ParamToCoords(LPARAM param, out int x, out int y)
    {
        x = (short) (ushort) (param.Value & 0xFFFF);
        y = (short) (ushort) (param.Value >> 16 & 0xFFFF);
    }

    private static LPARAM CoordsToParam(int x, int y)
    {
        nint value = 0;
        value |= ((ushort) (short) x) & 0xFFFF;
        value |= ((ushort) (short) y) >> 16 & 0xFFFF;

        return value;
    }

    private void Invoke(WndProcEventArgs args)
    {
        if (Service.Plugin is not { } plugin)
        {
            return;
        }

        if (WM.WM_MOUSEFIRST <= args.Message && args.Message <= WM.WM_MOUSELAST)
        {
            ParamToCoords(args.LParam, out int x, out int y);

#if false
            Service.PluginLog.Debug($"WM_MOUSE A @ {x} {y}");
#endif

            plugin.ConvertCoordsWinToGame(ref x, ref y);

#if false
            Service.PluginLog.Debug($"WM_MOUSE B @ {x} {y}");
#endif

            args.LParam = CoordsToParam(x, y);
        }

        if (args.Message == WM.WM_NCCALCSIZE && args.WParam != 0 && plugin.CurrentBorderlessFullscreen &&
            Service.Config.DXVKDWMHackMode >= DXVKDWMHackMode.SetClientEdge)
        {
            NCCALCSIZE_PARAMS* ncsize = (NCCALCSIZE_PARAMS*) args.LParam;
            MONITORINFO monitorInfo = new()
            {
                cbSize = (uint) sizeof(MONITORINFO)
            };

            if (MonitorFromWindow(plugin.CurrentHWND, MONITOR.MONITOR_DEFAULTTONEAREST) is { } monitor && monitor != HMONITOR.NULL &&
                GetMonitorInfo(monitor, &monitorInfo))
            {
                ncsize->rgrc[0] = monitorInfo.rcMonitor;

                args.SuppressCall = true;
            }
        }
    }

    private void Apply()
    {
        if (!_manager.Refresh())
        {
            return;
        }

        if (_applied)
        {
            Dispose();
        }

        if (_manager.PreWndProc is not { } preWndProcProp)
        {
            Service.PluginLog.Information("CustomResolution couldn't obtain the PreWndProc event.");

            return;
        }

        if (_genDelegate is null)
        {
            var delegateType = preWndProcProp.EventHandlerType!;
            var delegateInvoke = delegateType.GetMethod("Invoke")!;

            _genMethod = new DynamicMethod(
                "CustomResolution_PreWndProc",
                delegateInvoke.ReturnType,
                delegateInvoke.GetParameters().Select(p => p.ParameterType).ToArray()
            );

            var il = _genMethod.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(WndProcHook).GetMethod("InvokeStatic")!);
            il.Emit(OpCodes.Ret);

            _genDelegate = _genMethod.CreateDelegate(delegateType);
        }

        preWndProcProp.AddEventHandler(_manager.ProxiedValue, _genDelegate);

        _applied = true;
    }
}
