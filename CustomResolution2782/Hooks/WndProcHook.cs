using CustomResolution.WndProcHookManagerProxyApi;
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
    private DynamicMethod? _genMethodPre;
    private Delegate? _genDelegatePre;
    private DynamicMethod? _genMethodPost;
    private Delegate? _genDelegatePost;

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

        if (_manager.PreWndProc is { } preWndProcProp)
        {
            preWndProcProp.RemoveEventHandler(_manager.ProxiedValue, _genDelegatePre);
        }
        else
        {
            Service.PluginLog.Information("CustomResolution couldn't obtain the PreWndProc event.");
        }

        if (_manager.PostWndProc is { } postWndProcProp)
        {
            postWndProcProp.RemoveEventHandler(_manager.ProxiedValue, _genDelegatePost);
        }
        else
        {
            Service.PluginLog.Information("CustomResolution couldn't obtain the PostWndProc event.");
        }

        _applied = false;
    }

    public static void InvokeStaticPre(object args)
    {
        if (_instance is { } instance)
        {
            instance.InvokePre(new WndProcEventArgs(instance._manager, args));
        }
    }

    public static void InvokeStaticPost(object args)
    {
        if (_instance is { } instance)
        {
            instance.InvokePost(new WndProcEventArgs(instance._manager, args));
        }
    }

    public static void ParamToCoords(LPARAM param, out int x, out int y)
    {
        x = (short) (ushort) (param.Value & 0xFFFF);
        y = (short) (ushort) ((param.Value >> 16) & 0xFFFF);
    }

    public static LPARAM CoordsToParam(int x, int y)
    {
        nint value = 0;
        value |= ((ushort) (short) x) & 0xFFFF;
        value |= (((ushort) (short) y) & 0xFFFF) << 16;

        return value;
    }

    public static void ParamToSize(LPARAM param, out uint x, out uint y)
    {
        x = (ushort) (param.Value & 0xFFFF);
        y = (ushort) ((param.Value >> 16) & 0xFFFF);
    }

    public static LPARAM SizeToParam(uint x, uint y)
    {
        nint value = 0;
        value |= ((ushort) x) & 0xFFFF;
        value |= (((ushort) y) & 0xFFFF) << 16;

        return value;
    }

    private void InvokePre(WndProcEventArgs args)
    {
        if (Service.Plugin.Unloading)
        {
            return;
        }

        if (args.Message == WM.WM_WINDOWPOSCHANGING &&
            Service.Config._.MinSizeMode == MinSizeMode.Unlocked)
        {
            args.SuppressCall = true;
        }

        if (args.Message == WM.WM_WINDOWPOSCHANGED)
        {
            var windowpos = (WINDOWPOS*) args.LParam;
            Service.PluginLog.Debug($"WM_WINDOWPOSCHANGED {windowpos->cx} x {windowpos->cy}");
        }

        if (args.Message == WM.WM_SIZE)
        {
            ParamToCoords(args.LParam, out int x, out int y);
            Service.PluginLog.Debug($"WM_SIZE {x} x {y}");
            Service.DisplaySize.Update();
            args.LParam = SizeToParam(Service.DisplaySize.CurrentWidth, Service.DisplaySize.CurrentHeight);
        }

        if (WM.WM_MOUSEFIRST <= args.Message && args.Message <= WM.WM_MOUSELAST)
        {
            ParamToCoords(args.LParam, out int x, out int y);

#if false
            Service.PluginLog.Debug($"WM_MOUSE A @ {x} {y}");
#endif

            Service.DisplaySize.ConvertCoordsWinToGame(ref x, ref y);

#if false
            Service.PluginLog.Debug($"WM_MOUSE B @ {x} {y}");
#endif

            args.LParam = CoordsToParam(x, y);
        }

        if (args.Message == WM.WM_NCCALCSIZE && args.WParam != 0 && Service.DisplaySize.CurrentBorderlessFullscreen &&
            Service.Config._.DXVKDWMHackMode.IsSetClientEdge())
        {
            var ncsize = (NCCALCSIZE_PARAMS*) args.LParam;
            MONITORINFO monitorInfo = new()
            {
                cbSize = (uint) sizeof(MONITORINFO)
            };

            if (MonitorFromWindow(Service.DisplaySize.CurrentHWND, MONITOR.MONITOR_DEFAULTTONEAREST) is { } monitor && monitor != HMONITOR.NULL &&
                GetMonitorInfo(monitor, &monitorInfo))
            {
                ncsize->rgrc[0] = monitorInfo.rcMonitor;

                switch (Service.Config._.DXVKDWMHackMode)
                {
                    case DXVKDWMHackMode.SetClientEdgeResize:
                        ncsize->rgrc[0].bottom += 1;
                        break;

                    case DXVKDWMHackMode.SetClientEdgeBorder:
                        ncsize->rgrc[0].left += 1;
                        ncsize->rgrc[0].top += 1;
                        ncsize->rgrc[0].right -= 1;
                        ncsize->rgrc[0].bottom -= 1;
                        break;
                }

                args.SuppressCall = true;
            }

            // TODO: Check if border + repaing nc area to black works? Otherwise unset composited
        }
    }

    private void InvokePost(WndProcEventArgs args)
    {
        if (Service.Plugin.Unloading)
        {
            return;
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

        if (_manager.PreWndProc is not { } preWndProcProp ||
            _manager.PostWndProc is not { } postWndProcProp)
        {
            Service.PluginLog.Information("CustomResolution couldn't obtain the PreWndProc / PostWndProc events.");

            return;
        }

        if (_genDelegatePre is null)
        {
            var delegateType = preWndProcProp.EventHandlerType!;
            var delegateInvoke = delegateType.GetMethod("Invoke")!;

            _genMethodPre = new DynamicMethod(
                "CustomResolution_PreWndProc",
                delegateInvoke.ReturnType,
                delegateInvoke.GetParameters().Select(p => p.ParameterType).ToArray()
            );

            var il = _genMethodPre.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(WndProcHook).GetMethod(nameof(InvokeStaticPre))!);
            il.Emit(OpCodes.Ret);

            _genDelegatePre = _genMethodPre.CreateDelegate(delegateType);
        }

        if (_genDelegatePost is null)
        {
            var delegateType = preWndProcProp.EventHandlerType!;
            var delegateInvoke = delegateType.GetMethod("Invoke")!;

            _genMethodPost = new DynamicMethod(
                "CustomResolution_PostWndProc",
                delegateInvoke.ReturnType,
                delegateInvoke.GetParameters().Select(p => p.ParameterType).ToArray()
            );

            var il = _genMethodPost.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(WndProcHook).GetMethod(nameof(InvokeStaticPost))!);
            il.Emit(OpCodes.Ret);

            _genDelegatePost = _genMethodPost.CreateDelegate(delegateType);
        }

        preWndProcProp.AddEventHandler(_manager.ProxiedValue, _genDelegatePre);
        postWndProcProp.AddEventHandler(_manager.ProxiedValue, _genDelegatePost);

        _applied = true;
    }
}
