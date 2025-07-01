using CustomResolution.Hooks;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Config;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Component.Text;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using FloppyUtils;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public unsafe class GameSizeState : IDisposable
{
    private StringBuilder _dbg = new();
    private Hook<RTMApplyScaling> _rtmApplyScalingHook;
    private Hook<RTMDestroyAfterResize> _rtmDestroyAfterResizeHook;
    private Hook<RTMRegenAfterResize> _rtmRegenAfterResizeHook;
    private Hook<ICDX11ProcessCommands> _icdx11ProcessCommandsHook;

    private bool _wasEnabled = false;
    private object _renderLock = new();

    private ForceUpdateRTMState _forceUpdateRTM = ForceUpdateRTMState._0_Idle;

    public GameSizeState()
    {
        /* Writes DynamicResolutionTargetHeight to size[1] near the end,
         * but only if in (gpose || main menu), only scale < 1??
         *
         * RVAs:
         * 7.2: 140470150
         */
        _rtmApplyScalingHook = Service.GameInteropProvider.HookFromAddress<RTMApplyScaling>(
            Service.SigScanner.ScanText("48 89 5C 24 18 55 56 57 41 56 41 57 48 83 EC 20 8B 42 04 0F 57 C0 48 8B"),
            RTMApplyScalingDetour
        );
        _rtmApplyScalingHook.Enable();

        /* Device.RequestResolutionChange and other triggers run some callbacks, namely
         * (at the time of 7.2) 0x40 for RTM destruction and 0x48 for RTM reconstruction,
         * registered in RTM init and run in PostTick.
         *
         * We don't care about destruction as much because its behavior is unchanged, but
         * we do want to call it instead of triggering a slow full game resize,
         * which causes some annoyances with ReShade and possibly other external factors too.
         *
         * We care about reconstruction so that we can force the game to create larger RTs.
         * It's most notable for not being called directly (only via a shim that gets icalled),
         * and for being in the stack trace when dxgi updates the RT ptrs.
         */
        _rtmDestroyAfterResizeHook = Service.GameInteropProvider.HookFromAddress<RTMDestroyAfterResize>(
            Service.SigScanner.ScanText("40 53 48 83 EC 20 48 8B 05 ?? ?? ?? ?? 48 8B D9 80 78 7A 00 ?? ?? ?? ?? ?? ?? 48 89 6C 24"),
            RTMDestroyAfterResizeDetour
        );
        _rtmDestroyAfterResizeHook.Enable();
        _rtmRegenAfterResizeHook = Service.GameInteropProvider.HookFromAddress<RTMRegenAfterResize>(
            Service.SigScanner.ScanText("40 55 57 41 55 48 8D 6C 24 B9 48 81 EC A0 00 00 00 48 8B 05 50 10 2C 02 48 33 C4 48 89 45 27 4C"),
            RTMRegenAfterResizeDetour
        );
        _rtmRegenAfterResizeHook.Enable();

        /* ImmediateContextDX11.ProcessCommands can run in either DeviceX11.PostTick,
         * or in RenderThread.Run, which is prone to race conditions and crashes with manual RTM regen.
         * While we can regen RTMs after ProcessCommands is done, it would still leave us
         * with some other race conditions regarding the fields we modify.
         * Let's wrap it in a C# lock and call it a day...
         */
        _icdx11ProcessCommandsHook = Service.GameInteropProvider.HookFromAddress<ICDX11ProcessCommands>(
            Service.SigScanner.ScanText("48 89 5C 24 10 48 89 6C 24 18 56 57 41 56 48 83 EC 30 48 8B 01 41 8B F0 48 8B EA"),
            ICDX11ProcessCommandsDetour
        );
        _icdx11ProcessCommandsHook.Enable();
    }

    public bool ConfigDynRezo { get; private set; }
    public ResolutionScalingMode ConfigGraphicsRezoType { get; private set; }
    public float ConfigGraphicsRezoScale { get; private set; }

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }

    public string DebugInfo => _dbg.ToString();

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void RTMApplyScaling(RenderTargetManagerEx* rtm, uint* size, byte unk1);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void RTMDestroyAfterResize(RenderTargetManagerEx* rtm);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void RTMRegenAfterResize(RenderTargetManagerEx* rtm);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void ICDX11ProcessCommands(ImmediateContext* ctx, RenderCommandBufferGroup* cmds, uint count);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void DDX11PostTick(DeviceEx* dev);

    public void Dispose()
    {
        _rtmApplyScalingHook.Dispose();
        _rtmDestroyAfterResizeHook.Dispose();
        _rtmRegenAfterResizeHook.Dispose();
        _icdx11ProcessCommandsHook.Dispose();
        Service.Framework.RunOnFrameworkThread(Update);
    }

    public void Update()
    {
        _dbg.Clear();

        ref var cfg = ref Service.Config._.Game;

        ConfigDynRezo = Service.GameConfig.System.GetUInt(SystemConfigOption.DynamicRezoType.ToString()) != 0U;
        // GameConfig starts with FSR at 0; GraphicsConfig starts with FSR at 1
        ConfigGraphicsRezoType = (ResolutionScalingMode) (Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoUpscaleType.ToString()) + 1);
        ConfigGraphicsRezoScale = Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoScale.ToString()) / 100f;

        var dev = (DeviceEx*) Device.Instance();
        var gfx = (GraphicsConfigEx*) GraphicsConfig.Instance();
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        if (Service.DebugConfig.IsDebug)
        {
            _dbg.Append(@$"RTMApplyScaling 0x{(_rtmApplyScalingHook.IsDisposed ? null : _rtmApplyScalingHook.Address):X16}
RTMRegenAfterResize 0x{(_rtmRegenAfterResizeHook.IsDisposed ? null : _rtmRegenAfterResizeHook.Address):X16}
ICDX11ProcessCommands 0x{(_icdx11ProcessCommandsHook.IsDisposed ? null : _icdx11ProcessCommandsHook.Address):X16}
DR !{gfx->DynamicRezoEnable} ?{gfx->DynamicRezoEnableBeyond1} _{gfx->DynamicRezoEnableCutScene} ?{gfx->DynamicRezoEnableUnkx47} ?{gfx->DynamicRezoEnableUnkx48}
GR x{gfx->GraphicsRezoScale} ?{gfx->GraphicsRezoUnk1} _{gfx->GraphicsRezoUpscaleType} ?{gfx->GraphicsRezoUnk2}
DEV 0x{(long) (nint) dev:X16}
GFX 0x{(long) (nint) gfx:X16}
RTM 0x{(long) (nint) rtm:X16}
RTM {rtm->Resolution_Width} x {rtm->Resolution_Height}
RTM H {rtm->DynamicResolutionActualTargetHeight} {rtm->DynamicResolutionTargetHeight} {rtm->DynamicResolutionMaximumHeight} {rtm->DynamicResolutionMinimumHeight}
RTM S {rtm->GraphicsRezoScalePrev} {rtm->GraphicsRezoScaleGlassX} {rtm->GraphicsRezoScaleGlassY} {rtm->GraphicsRezoScaleGlassX} {rtm->GraphicsRezoScaleGlassY}
RR {dev->RequestRender} 0x{(long) dev->ImmediateContext->IfNonZeroSkipPostTickProcess:X16}
");
        }

        bool unloading = Service.Plugin.Unloading;
        bool enabled = !unloading && cfg.IsEnabled;

        if (enabled || _wasEnabled)
        {
            gfx->GraphicsRezoScale = enabled ? cfg.Scale : ConfigGraphicsRezoScale;
            gfx->DynamicRezoEnable = (byte) (ConfigDynRezo || enabled ? 1 : 0);
            if (enabled)
            {
                gfx->GraphicsRezoUpscaleType = (byte) (cfg.Scale <= 1f ? Service.Config._.ResolutionScalingMode : ResolutionScalingMode.Fast);
                rtm->DynamicResolutionMinimumHeight = rtm->DynamicResolutionMaximumHeight;
            }
            else
            {
                gfx->GraphicsRezoUpscaleType = (byte) ConfigGraphicsRezoType;
            }
            _wasEnabled = enabled;
        }

        var scale = MathF.Max(1f, enabled ? cfg.Scale : 1f);
        GetScaledWidthHeight(dev->Width, dev->Height, scale, out var widthS, out var heightS);

        // TODO: Figure out a more consistent way to get to the currently allocated size.
        // Check if the backing RTs are the expected size.
        var tex = rtm->_.Unk20[0].Value;
        if (tex->AllocatedWidth != widthS || tex->AllocatedHeight != heightS)
        {
            if (!unloading)
            {
                lock (_renderLock)
                {
                    Service.PluginLog.Debug($"Regenerating dirty RTM - locking ImmediateContextDX11.ProcessCommands");
                    dev->RequestResolutionChange = 1;
                    RTMDestroyAfterResizeDetour(rtm);
                    RTMRegenAfterResizeDetour(rtm);
                    dev->RequestResolutionChange = 0;
                }
            }
            else
            {
                dev->RequestResolutionChange = 1;
            }
        }

        // Check if the RTM size didn't go back to the screen size when switching f.e. in / out of gpose.
        if (scale > 1f && _forceUpdateRTM == ForceUpdateRTMState._0_Idle &&
            (rtm->Resolution_Width != widthS || rtm->Resolution_Height != heightS))
        {
            // Can also be forced via dev->RequestResolutionChange, but while cleaner on paper, it trips up ReShade.
            Service.PluginLog.Debug("_forceUpdateRTM -> 1");
            _forceUpdateRTM = ForceUpdateRTMState._1_FakeHalf;
        }

        switch (enabled ? _forceUpdateRTM : ForceUpdateRTMState._0_Idle)
        {
            default:
            case ForceUpdateRTMState._0_Idle:
                break;

            case ForceUpdateRTMState._1_FakeHalf:
                gfx->DynamicRezoEnable = 0;
                gfx->GraphicsRezoScale = 0.5f;
                Service.PluginLog.Debug("_forceUpdateRTM -> 2");
                _forceUpdateRTM = ForceUpdateRTMState._2_ToScale;
                break;

            case ForceUpdateRTMState._2_ToScale:
                gfx->DynamicRezoEnable = 1;
                gfx->GraphicsRezoScale = scale;
                Service.PluginLog.Debug("_forceUpdateRTM -> 0");
                _forceUpdateRTM = ForceUpdateRTMState._0_Idle;
                // TODO: Figure out what's going on with GraphicsRezoScaleGlassXY and GraphicsRezoUnk1
                break;
        }

        CurrentWidth = rtm->Resolution_Width;
        CurrentHeight = rtm->Resolution_Height;
    }

    private void RTMApplyScalingDetour(RenderTargetManagerEx* rtm, uint* size, byte unk1)
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
        {
            _rtmApplyScalingHook.OriginalDisposeSafe(rtm, size, unk1);
            return;
        }

        var gfx = (GraphicsConfigEx*) GraphicsConfig.Instance();
        var _DynamicRezoEnableBeyond1 = gfx->DynamicRezoEnableBeyond1;
        gfx->DynamicRezoEnableBeyond1 = 1;

        Service.PluginLog.Debug($"Applying scaling, before: {size[0]} {size[1]} {unk1}");
        _rtmApplyScalingHook.OriginalDisposeSafe(rtm, size, unk1);
        Service.PluginLog.Debug($"Applying scaling, after: {size[0]} {size[1]} {unk1}");

        gfx->DynamicRezoEnableBeyond1 = _DynamicRezoEnableBeyond1;
    }

    private void RTMDestroyAfterResizeDetour(RenderTargetManagerEx* rtm)
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
        {
            _rtmDestroyAfterResizeHook.OriginalDisposeSafe(rtm);
            return;
        }

        Service.PluginLog.Debug($"Destroying RTM resources");
        _rtmDestroyAfterResizeHook.OriginalDisposeSafe(rtm);
        Service.PluginLog.Debug($"After: 0x{(long) (nint) rtm->_.Unk20[0].Value->D3D11Texture2D:X16}");
    }

    private void RTMRegenAfterResizeDetour(RenderTargetManagerEx* rtm)
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
        {
            _rtmRegenAfterResizeHook.OriginalDisposeSafe(rtm);
            return;
        }

        var dev = (DeviceEx*) Device.Instance();

        var _Width = dev->Width;
        var _Height = dev->Height;

        var scale = MathF.Max(1f, cfg.Scale);
        GetScaledWidthHeight(_Width, _Height, scale, out dev->Width, out dev->Height);

        Service.PluginLog.Debug($"Regenerating RTM resources: {dev->Width} x {dev->Height}");
        _rtmRegenAfterResizeHook.OriginalDisposeSafe(rtm);
        Service.PluginLog.Debug($"After: 0x{(long) (nint) rtm->_.Unk20[0].Value->D3D11Texture2D:X16}");

        dev->Width = _Width;
        dev->Height = _Height;
    }

    private void ICDX11ProcessCommandsDetour(ImmediateContext* ctx, RenderCommandBufferGroup* cmds, uint count)
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
        {
            _icdx11ProcessCommandsHook.OriginalDisposeSafe(ctx, cmds, count);
            return;
        }

        lock (_renderLock)
        {
            _icdx11ProcessCommandsHook.OriginalDisposeSafe(ctx, cmds, count);
        }
    }

    private void GetScaledWidthHeight(uint width, uint height, float scale, out uint widthS, out uint heightS)
    {
        heightS = (uint) MathF.Round(height * scale);
        widthS = (width * heightS) / height;
    }

    private enum ForceUpdateRTMState
    {
        _0_Idle,
        _1_FakeHalf,
        _2_ToScale
    }
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct GraphicsConfigEx
{
    [FieldOffset(0)]
    public GraphicsConfig _;

// CRES_CLEAN never
    public ref byte DynamicRezoEnable => ref _.DynamicRezoEnable;
    // Set to 0 in main menu and gpose, preventing scaling beyond 1.
    public ref byte DynamicRezoEnableBeyond1 => ref RefPtr.For(ref DynamicRezoEnable).Offs<byte>(0x1).Ref;
    public ref byte DynamicRezoEnableCutScene => ref RefPtr.For(ref DynamicRezoEnable).Offs<byte>(0x2).Ref;
    public ref byte DynamicRezoEnableUnkx47 => ref RefPtr.For(ref DynamicRezoEnable).Offs<byte>(0x3).Ref;
    public ref byte DynamicRezoEnableUnkx48 => ref RefPtr.For(ref DynamicRezoEnable).Offs<byte>(0x4).Ref;

    public ref float GraphicsRezoScale => ref _.GraphicsRezoScale;
    public ref float GraphicsRezoUnk1 => ref RefPtr.For(ref GraphicsRezoScale).Offs<float>(0x4).Ref;
    public ref byte GraphicsRezoUpscaleType => ref _.GraphicsRezoUpscaleType;
    public ref byte GraphicsRezoUnk2 => ref RefPtr.For(ref GraphicsRezoUpscaleType).Offs<byte>(0x1).Ref;
}

// RenderTargetManager is updated very sporadically, and some fields we need flew out.
// https://github.com/aers/FFXIVClientStructs/commit/589df2aa5cd9c98b4d62269034cd6da903f49b5f#diff-8e7d9b03cb91cb07a8d7b463b5be4672793a328703bde393e7acd890822a72cf
[StructLayout(LayoutKind.Explicit)]
public unsafe struct RenderTargetManagerEx
{
    [FieldOffset(0)]
    public RenderTargetManager _;

    [FieldOffset(0x428)]
    public uint Resolution_Width;
    [FieldOffset(0x428 + 0x4)]
    public uint Resolution_Height;

    [FieldOffset(0x6F0 + 2 * 0)]
    public ushort DynamicResolutionActualTargetHeight;
    [FieldOffset(0x6F0 + 2 * 1)]
    public ushort DynamicResolutionTargetHeight;
    [FieldOffset(0x6F0 + 2 * 2)]
    public ushort DynamicResolutionMaximumHeight;
    [FieldOffset(0x6F0 + 2 * 3)]
    public ushort DynamicResolutionMinimumHeight;

    [FieldOffset(0x70C + 4 * 0)]
    public float GraphicsRezoScalePrev;
    [FieldOffset(0x70C + 4 * 1)]
    public float GraphicsRezoScaleGlassX;
    [FieldOffset(0x70C + 4 * 2)]
    public float GraphicsRezoScaleGlassY;
    [FieldOffset(0x70C + 4 * 3)]
    public float GraphicsRezoScaleUnk1;
    [FieldOffset(0x70C + 4 * 4)]
    public float GraphicsRezoScaleUnk2;
}
