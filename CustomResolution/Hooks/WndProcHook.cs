using CustomResolution.WndProcHookManagerProxyApi;
using Serilog.Events;
using System;
using System.Linq;
using System.Reflection.Emit;
using TerraFX.Interop.Windows;


namespace CustomResolution.Hooks;

// THIS. IS. UGLY.
public sealed class WndProcHook : IDisposable
{
    private const uint WM_MOUSEFIRST = 0x0200;
    private const uint WM_MOUSELAST = 0x0209;

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
        if (!(WM_MOUSEFIRST <= args.Message && args.Message <= WM_MOUSELAST))
        {
            return;
        }

        ParamToCoords(args.LParam, out int x, out int y);

#if false
        Service.PluginLog.Debug($"WM_MOUSE A @ {x} {y}");
#endif

        Service.Plugin.ConvertCoordsWinToGame(ref x, ref y);

#if false
        Service.PluginLog.Debug($"WM_MOUSE B @ {x} {y}");
#endif

        args.LParam = CoordsToParam(x, y);
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
