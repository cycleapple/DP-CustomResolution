using Dalamud.Game.Config;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FloppyUtils;
using FloppyUtils.Graphics;
using FloppyUtils.StructEx.Graphics;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using TerraFX.Interop.DirectX;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using SDX11 = SharpDX.Direct3D11;

namespace CustomResolution;

public unsafe sealed class GameSizeState
{
    private readonly List<IDalamudHook> _hooks = [];
    private readonly Hook<RTMApplyScaling> _rtmApplyScalingHook;
    private readonly Hook<RTMDestroyAfterResize> _rtmDestroyAfterResizeHook;
    private readonly Hook<RTMRegenAfterResize> _rtmRegenAfterResizeHook;
    private readonly Hook<ImmediateProcessCommands> _immediateProcessCommandsHook;
    private readonly Hook<TaskRenderGraphicsRender> _taskRenderGraphicsRenderHook;
    private readonly Hook<PrepareTexture> _prepareTextureHook;
    private readonly Hook<ImmediateBindPSSRVs> _immediateBindPSSRVsHook;
    private readonly Hook<ImmediateBindCSSRVs> _immediateBindCSSRVsHook;
    private readonly Hook<CreateTexture2D> _createTexture2DHook;

    private readonly SDX11.DeviceContext _d3dctx;
    private readonly SDX11.Device _d3ddev;

    private readonly StringBuilder _dbg = new();
    private readonly Lock _renderLock = new();

    private readonly SDX11.SamplerState _samplerMips;
    private readonly SDX11.SamplerState _samplerPoint;
    private readonly SDX11.SamplerState _samplerLinear;

    private bool _wasEnabled = false;

    private ForceUpdateRTMState _forceUpdateRTM = ForceUpdateRTMState._0_Idle;

    private ResolutionScalingMode? _setConfigGraphicsRezoType;

    public GameSizeState()
    {
        var pfx = PostEffectManagerEx.Instance();
        FService.PluginLog.Debug($"PostEffectManager: 0x{(long) (nint) pfx:X16}");
        FService.PluginLog.Info($"DLSS: {(pfx->DLSS != null ? "Available" : "Unavailable")}");

        var dev = (DeviceEx*) Device.Instance();
        _d3dctx = CppObject.FromPointer<SDX11.DeviceContext>((nint) dev->D3D11DeviceContext);
        _d3ddev = _d3dctx.Device;

        /* Writes DynamicResolutionTargetHeight to size[1] near the end,
         * but if in (gpose || main menu), only scale < 1??
         */
        _hooks.Add(_rtmApplyScalingHook = FService.GameInteropProvider.HookFromAddress<RTMApplyScaling>(
            // Function
            FService.SigScanner.ScanText("48 89 5C 24 18 55 56 57 41 56 41 57 48 83 EC 20 8B 42 04 0F 57 C0 48 8B")
                // Call site, currently unknown what exactly it belongs to
                .SameAddr(FService.SigScanner.ScanRVASig(new("48 89 5C 24 48| 45 33 C0| 48 8B 1D ?? ?? ?? ??| 48 8B F1| 48 89 6C 24 50| 4C 89 74 24 20| 4C 8D B1 28 04 00 00| 49 8B D6| 48 8B 83 8C 00 00 00| 49 89 06| (E8 *95 08 00 00)| 48 8B 05 ?? ?? ?? ??| B9 01 00 00 00| 0F B6 50 33| 41 8B 06|"))),
            RTMApplyScalingDetour
        ));

        /* Device.RequestResolutionChange and other triggers run some callbacks, namely
         * (at the time of 7.2) 0x40 for RTM destruction and 0x48 for RTM reconstruction,
         * registered in RTM init and run in PostTick.
         *
         * RenderTargetManager.Initialize calls subroutines to install those callbacks.
         *
         * We don't care about destruction as much because its behavior is unchanged, but
         * we do want to call it instead of triggering a slow full game resize,
         * which causes some annoyances with ReShade and possibly other external factors too.
         *
         * We care about reconstruction so that we can force the game to create larger RTs.
         * It's most notable for not being called directly (only via a shim that gets icalled),
         * and for being in the stack trace when dxgi updates the RT ptrs.
         */
        var rtmCallbacks = new MultiRVASig("89 81 2C 04 00 00| B8 56 55 55 55| 41 F7 E8| 45 33 C0| 8B C2| C1 E8 1F| 03 D0| 66 89 91 F6 06 00 00| 48 8D 15 ?? ?? ?? ??| 48 8B CB| E8 ?? ?? ?? ??| 45 33 C0| 89 87 60 04 00 00| 48 8D 15 ?? ?? ?? ??| 48 8B CB| E8 ?? ?? ?? ??| 45 33 C0| 89 87 64 04 00 00| (48 8D 15 *?? ?? ?? ??)| 48 8B CB| E8 ?? ?? ?? ??| 45 33 C0| 89 87 68 04 00 00| (48 8D 15 *?? ?? ?? ??)| 48 8B CB| E8 ?? ?? ?? ??| 89 87 6C 04 00 00| 41 BD 01 00 00 00| 48 8B 15 ?? ?? ?? ??| 41 8B CD| 41 BF 50 14 00 00| C7 44 24 28 03 00 00 00| 41 B9 50 82 00 00| C7 44 24 20 08 00 00 00| 45 8B C5| 0F B6 42 33| 2B C8|");
        _hooks.Add(_rtmDestroyAfterResizeHook = FService.GameInteropProvider.HookFromAddress<RTMDestroyAfterResize>(
            dev->RenderTargetManagerResizeDestroyCallback->Slots[0].Function.SameAddr(FService.SigScanner.ScanRVASig(rtmCallbacks.Sigs[0])),
            RTMDestroyAfterResizeDetour
        ));
        _hooks.Add(_rtmRegenAfterResizeHook = FService.GameInteropProvider.HookFromAddress<RTMRegenAfterResize>(
            dev->RenderTargetManagerResizeRegenCallback->Slots[0].Function.SameAddr(FService.SigScanner.ScanRVASig(rtmCallbacks.Sigs[1])),
            RTMRegenAfterResizeDetour
        ));

        /* ImmediateContextDX11.ProcessCommands can run in either DeviceX11.PostTick,
         * or in RenderThread.Run, which is prone to race conditions and crashes with manual RTM regen.
         * While we can regen RTMs after ProcessCommands is done, it would still leave us
         * with some other race conditions regarding the fields we modify.
         * Let's wrap it in a C# lock and call it a day...
         *
         * Identifiable by being called by DeviceDX11.PostTick and RenderThread.Run
         */
        _hooks.Add(_immediateProcessCommandsHook = FService.GameInteropProvider.HookFromAddress<ImmediateProcessCommands>(
            // DeviceX11.PostTick call site
            FService.SigScanner.ScanRVASig(new("8B 86 88 00 00 00| 85 C0| 75 0C| C7 86 88 00 00 00 01 00 00 00| EB 05| 83 F8 01| 75 1A| 44 8B 86 10 09 00 00| 48 8B 96 08 09 00 00| 48 8B 8E C0 0A 0E 00| (E8 *?? ?? ?? ??)| 48 8B 8E B0 0A 0E 00| 48 8B 01| FF 90 70 03 00 00| 83 BE 88 00 00 00 01| 75 47|"))
                // RenderThread.Run call site
                .SameAddr(FService.SigScanner.ScanRVASig(new("48 8B 4B 28| BA FF FF FF FF| FF 15 ?? ?? ?? ??| 0F B6 43 20| 84 C0| 75 3C| 48 8B 0D ?? ?? ?? ??| 83 B9 88 00 00 00 01| 75 1A| 44 8B 81 10 09 00 00| 48 8B 91 08 09 00 00| 48 8B 89 C0 0A 0E 00| (E8 *9E F1 FF FF) 48 8B 4B 30| FF 15 ?? ?? ?? ??|"))),
            ImmediateProcessCommandsDetour
        ));

        /* TaskRenderGraphicsRender has got a prio of 0x1F and seems to be the only task with that prio.
         * Hooking this is relevant for post-processing, and can be validated easily by enabling TSCMAA at >1x scale.
         */
        Task* taskRenderGraphicsRenderTask = null;
        for (var task = &TaskManager.Instance()->TaskList[0x1F].Task; task != null; task = task->Next)
        {
            if (task->Runner != null)
            {
                taskRenderGraphicsRenderTask = task;
                break;
            }
        }
        // TODO: Add pointer verification
        FService.PluginLog.Debug($"TaskRenderGraphicsRender Task @ 0x{(long) taskRenderGraphicsRenderTask:X16} -> 0x{(long) (nint) taskRenderGraphicsRenderTask->Func:X16}");
        _hooks.Add(_taskRenderGraphicsRenderHook = FService.GameInteropProvider.HookFromAddress<TaskRenderGraphicsRender>(
            taskRenderGraphicsRenderTask->Func,
            TaskRenderGraphicsRenderDetour
        ));

        /* Texture preparation used in a lot of places, but most notable for
         * writing to the mip level count of the gameplay texture and also updating the D3D11 ptrs.
         */
        _hooks.Add(_prepareTextureHook = FService.GameInteropProvider.HookFromAddress<PrepareTexture>(
            FService.SigScanner.ScanRVASig(new("48 8D 4B 20| E8 ?? ?? ?? ??| 48 89 5F 58| 48 8B 4D A7| 48 85 C9| 74 0A| 48 8B 01| FF 50 10| 4C 89 65 A7| 44 38 67 48| 74 4E| 48 8B 4F 60| 48 8D 57 38| 44 8B 4F 4C| 41 B8 01 00 00 00| 48 85 C9| 74 0B| (E8 *?? ?? ?? ??)| 84 C0| 75 2E| EB 8E| 8B 47 50| 48 8B 0D ?? ?? ?? ??| 0F BA E8 15| C7 44 24 28 01 00 00 00| 89 44 24 20| E8 ?? ?? ?? ??| 48 85 C0|"))
                .SameAddr(FService.SigScanner.ScanRVASig(new("48 89 9C 24 C8 00 00 00| 49 8D 95 8C 00 00 00| 48 8B 02| 48 8D 99| 28 04 00 00| 48 89 B4 24 D0 00 00 00| 48 89 03| 48 8B 05 ?? ?? ?? ??| 4C 89 A4 24 D8 00 00 00| 4C 89 B4 24 98 00 00 00| 4C 89 BC 24 90 00 00 00| 80 78 7A 00| 74 20| 48 8B 89 70 03 00 00| 48 85 C9| 74 14| 45 33 C9| 45 8D 41 01| (E8 *?? ?? ?? ??)| 84 C0| 0F 84 07 01 00 00|"))),
            PrepareTextureDetour
        ));

        /* The shader resource view of GameplayTexture gets read by these two.
         * The two functions should be identical with similar usage patterns, but read data from different offsets.
         * (This trips up some signature scripts for Ghidra, such as Sigga.)
         */
        // TODO: Add pointer verification
        _hooks.Add(_immediateBindPSSRVsHook = FService.GameInteropProvider.HookFromAddress<ImmediateBindPSSRVs>(
            FService.SigScanner.ScanText("48 8B C4 4C 89 40 18 48 89 48 08 53 56 48 83 EC 68 44 8B 5A 44 48 8D B1 E8 03 00 00 4C 89 68 D8").Log(),
            ImmediateBindPSSRVsDetour
        ));
        _hooks.Add(_immediateBindCSSRVsHook = FService.GameInteropProvider.HookFromAddress<ImmediateBindCSSRVs>(
            FService.SigScanner.ScanText("48 8B C4 4C 89 40 18 48 89 48 08 53 56 48 83 EC 68 44 8B 5A 44 48 8D B1 E8 13 00 00 4C 89 68 D8").Log(),
            ImmediateBindCSSRVsDetour
        ));

        /* Hook CreateTexture2D to forcibly set the mipmap generation flag.
         */
        _hooks.Add(_createTexture2DHook = FService.GameInteropProvider.HookFromAddress<CreateTexture2D>(
            ((ID3D11Device*) _d3ddev.NativePointer)->lpVtbl[5],
            CreateTexture2DDetour
        ));

        var samplerDesc = SDX11.SamplerStateDescription.Default();
        samplerDesc.Filter = SDX11.Filter.MinMagMipLinear;
        samplerDesc.AddressU = SDX11.TextureAddressMode.Mirror;
        samplerDesc.AddressV = SDX11.TextureAddressMode.Mirror;
        samplerDesc.AddressW = SDX11.TextureAddressMode.Mirror;
        _samplerMips = new(_d3ddev, samplerDesc);

        samplerDesc.Filter = SDX11.Filter.MinMagMipPoint;
        samplerDesc.MinimumLod = 0;
        samplerDesc.MaximumLod = 0;
        _samplerPoint = new(_d3ddev, samplerDesc);

        samplerDesc.Filter = SDX11.Filter.MinMagLinearMipPoint;
        _samplerLinear = new(_d3ddev, samplerDesc);

        foreach (var hook in _hooks)
        {
            hook.Enable();
        }
    }

    private delegate void RTMApplyScaling(RenderTargetManagerEx* rtm, uint* size, byte unk1);
    private delegate byte RTMDestroyAfterResize();
    private delegate void RTMRegenAfterResize();
    private delegate void ImmediateProcessCommands(ImmediateContextEx* ctx, RenderCommandBufferGroup* cmds, uint count);
    private delegate void DDX11PostTick(DeviceEx* dev);
    private delegate void TaskRenderGraphicsRender();
    private delegate ulong PrepareTexture(Texture* tex, uint* size, byte mips, uint format);
    private delegate void ImmediateBindPSSRVs(ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data);
    private delegate void ImmediateBindCSSRVs(ImmediateContextEx* im, Shader* cs, ImmediateSRV* data);
    private delegate int CreateTexture2D(ID3D11Device* d3ddev, D3D11_TEXTURE2D_DESC* desc, D3D11_SUBRESOURCE_DATA* initialData, ID3D11Texture2D** tex);

    public bool ConfigDynRezo { get; private set; }
    public ResolutionScalingMode ConfigGraphicsRezoType { get; private set; }
    public float ConfigGraphicsRezoScale { get; private set; }

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }

    public string DebugInfo => _dbg.ToString();

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            hook.Dispose();
        }

        lock (_renderLock)
        {
            _samplerLinear.Dispose();
            _samplerPoint.Dispose();
            _samplerMips.Dispose();
        }

        _d3ddev.Dispose();
    }

    public void SetConfigGraphicsRezoType(ResolutionScalingMode mode)
    {
        _setConfigGraphicsRezoType = mode;
    }

    public void Update()
    {
        _dbg.Clear();

        ref var cfg = ref Service.Config._.Game;

        if (_setConfigGraphicsRezoType is { } setType)
        {
            _setConfigGraphicsRezoType = null;
            Service.GameConfig.System.Set(SystemConfigOption.GraphicsRezoUpscaleType.ToString(), setType.ToXIVSysConf());
        }

        ConfigDynRezo = Service.GameConfig.System.GetUInt(SystemConfigOption.DynamicRezoType.ToString()) != 0U;
        // GameConfig starts with FSR at 0; GraphicsConfig starts with FSR at 1
        ConfigGraphicsRezoType = ResolutionScalingModeExt.FromXIVSysConf(Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoUpscaleType.ToString()));
        ConfigGraphicsRezoScale = Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoScale.ToString()) / 100f;

        var dev = (DeviceEx*) Device.Instance();
        var gfx = (GraphicsConfigEx*) GraphicsConfig.Instance();
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        if (Service.DebugConfig.IsDebug)
        {
            _dbg.Append(@$"DR !{gfx->DynamicRezoEnable} +{gfx->DynamicRezoEnableBeyond1} _{gfx->DynamicRezoEnableCutScene} ?{gfx->DynamicRezoEnableUnkx47} ?{gfx->DynamicRezoEnableUnkx48}
GR x{gfx->GraphicsRezoScale} ?{gfx->GraphicsRezoUnk1} _{gfx->GraphicsRezoUpscaleType} ?{gfx->GraphicsRezoUnk2}
RTM {rtm->Resolution_Width} x {rtm->Resolution_Height}
RTM H {rtm->DynamicResolutionActualTargetHeight} {rtm->DynamicResolutionTargetHeight} {rtm->DynamicResolutionMaximumHeight} {rtm->DynamicResolutionMinimumHeight}
RTM S {rtm->GraphicsRezoScalePrev} {rtm->GraphicsRezoScaleGlassX} {rtm->GraphicsRezoScaleGlassY} {rtm->GraphicsRezoScaleGlassX} {rtm->GraphicsRezoScaleGlassY}
RR {dev->RequestRender} 0x{(long) dev->ImmediateContext->IfNonZeroSkipPostTickProcess:X16}
");
        }

        bool unloading = FService.Unloading;
        bool enabled = !unloading && cfg.IsEnabled;

        if (enabled || _wasEnabled)
        {
            gfx->GraphicsRezoScale = enabled ? cfg.Scale : ConfigGraphicsRezoScale;
            gfx->DynamicRezoEnable = (byte) (ConfigDynRezo || enabled ? 1 : 0);
            if (enabled)
            {
                gfx->GraphicsRezoUpscaleType = cfg.Scale <= 1f ? Service.Config._.ResolutionScalingMode.ToXIVGFX() : ResolutionScalingMode.Linear.ToXIVGFX();
                rtm->DynamicResolutionMinimumHeight = rtm->DynamicResolutionMaximumHeight;
            }
            else
            {
                gfx->GraphicsRezoUpscaleType = ConfigGraphicsRezoType.ToSupported().ToXIVGFX();
            }
            _wasEnabled = enabled;
        }

        var scale = MathF.Max(1f, enabled ? cfg.Scale : 1f);
        GetScaledWidthHeight(dev->Width, dev->Height, scale, out var widthS, out var heightS);

        // Check if the backing RTs are the expected size.
        var tex = rtm->GameplayTextureUnk3;
        if (tex->AllocatedWidth != widthS || tex->AllocatedHeight != heightS)
        {
            if (!unloading)
            {
                Service.PluginLog.Debug($"Regenerating dirty RTM - locking ImmediateContextDX11.ProcessCommands");
                lock (_renderLock)
                {
                    Service.PluginLog.Debug($"Regenerating dirty RTM - locked ImmediateContextDX11.ProcessCommands");
                    dev->RequestResolutionChange = 1;
                    RTMDestroyAfterResizeDetour();
                    RTMRegenAfterResizeDetour();
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
            Service.PluginLog.Debug($"_forceUpdateRTM -> 1 - expected {widthS} x {heightS}, got {rtm->Resolution_Width} x {rtm->Resolution_Height}");
            _forceUpdateRTM = ForceUpdateRTMState._1_FakeInv;
        }

        switch (enabled ? _forceUpdateRTM : ForceUpdateRTMState._0_Idle)
        {
            default:
                if (_forceUpdateRTM < 0)
                {
                    _forceUpdateRTM++;
                }
                break;

            case ForceUpdateRTMState._0_Idle:
                break;

            case ForceUpdateRTMState._1_FakeInv:
                gfx->DynamicRezoEnable = 0;
                gfx->GraphicsRezoScale = MathF.Min(1f, 1f / scale);
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

    private static void GetScaledWidthHeight(uint width, uint height, float scale, out uint widthS, out uint heightS)
    {
        heightS = (uint) MathF.Round(height * scale, MidpointRounding.AwayFromZero);
        widthS = (width * heightS) / height;
    }

    private void RTMApplyScalingDetour(RenderTargetManagerEx* rtm, uint* size, byte unk1)
    {
        Debug.Assert(rtm == RenderTargetManager.Instance());

        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            _rtmApplyScalingHook.OriginalDisposeSafe(rtm, size, unk1);
            return;
        }

        var gfx = (GraphicsConfigEx*) GraphicsConfig.Instance();
        var _DynamicRezoEnableBeyond1 = gfx->DynamicRezoEnableBeyond1;
        gfx->DynamicRezoEnableBeyond1 = (byte) (cfg.Scale > 1f ? 1 : 0);

        Service.PluginLog.Debug($"Applying scaling, before: {size[0]} {size[1]} {unk1}");
        _rtmApplyScalingHook.OriginalDisposeSafe(rtm, size, unk1);
        Service.PluginLog.Debug($"Applying scaling, after: {size[0]} {size[1]} {unk1}");

        gfx->DynamicRezoEnableBeyond1 = _DynamicRezoEnableBeyond1;
    }

    private byte RTMDestroyAfterResizeDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            return _rtmDestroyAfterResizeHook.OriginalDisposeSafe();
        }

        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        Service.PluginLog.Debug($"Destroying RTM resources");
        byte rv = _rtmDestroyAfterResizeHook.OriginalDisposeSafe();
        Service.PluginLog.Debug($"After: {rv} 0x{(long) (nint) rtm->GameplayTextureUnk1->D3D11Texture2D:X16} 0x{(long) (nint) rtm->GameplayTextureUnk3->D3D11Texture2D:X16}");
        return rv;
    }

    private void RTMRegenAfterResizeDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            _rtmRegenAfterResizeHook.OriginalDisposeSafe();
            return;
        }

        var dev = (DeviceEx*) Device.Instance();
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        using var scale = new ScaleState();
        Service.PluginLog.Debug($"Regenerating RTM resources: {dev->Width} x {dev->Height}");
        _rtmRegenAfterResizeHook.OriginalDisposeSafe();
        Service.PluginLog.Debug($"After: 0x{(long) (nint) rtm->GameplayTextureUnk1->D3D11Texture2D:X16} 0x{(long) (nint) rtm->GameplayTextureUnk3->D3D11Texture2D:X16}");
    }

    private void ImmediateProcessCommandsDetour(ImmediateContextEx* ctx, RenderCommandBufferGroup* cmds, uint count)
    {
        lock (_renderLock)
        {
            _immediateProcessCommandsHook.OriginalDisposeSafe(ctx, cmds, count);
        }
    }

    private void TaskRenderGraphicsRenderDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            _taskRenderGraphicsRenderHook.OriginalDisposeSafe();
            return;
        }

        lock (_renderLock)
        {
            using var scale = new ScaleState();
            _taskRenderGraphicsRenderHook.OriginalDisposeSafe();
        }
    }

    private ulong PrepareTextureDetour(Texture* tex, uint* size, byte mips, uint format)
    {
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        if (tex != rtm->GameplayTextureUnk1 && tex != rtm->GameplayTextureUnk3)
        {
            return _prepareTextureHook.OriginalDisposeSafe(tex, size, mips, format);
        }

        ref var cfg = ref Service.Config._;

        if (!FService.Unloading && cfg.Game.IsEnabled && cfg.Game.Scale > 2f)
        {
            mips = (byte) MathF.Ceiling(MathF.Log2(cfg.Game.Scale));
        }

        Service.PluginLog.Debug($"Preparing gameplay texture 0x{(long) (nint) tex:X16} with {mips} mips, currently 0x{(long) (nint) tex->D3D11Texture2D:X16}");
        var rv = _prepareTextureHook.OriginalDisposeSafe(tex, size, mips, format);
        Service.PluginLog.Debug($"After: 0x{rv:X16} 0x{(long) (nint) tex->D3D11Texture2D:X16} 0x{(long) (nint) tex->D3D11ShaderResourceView:X16}");
        return rv;
    }

    private void ImmediateBindPSSRVsDetour(ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data)
    {
        _immediateBindPSSRVsHook.OriginalDisposeSafe(im, ps, data);
        PostImmediateBindSRVs(true, _d3dctx.PixelShader, im, ps, data);
    }

    private void ImmediateBindCSSRVsDetour(ImmediateContextEx* im, Shader* cs, ImmediateSRV* data)
    {
        _immediateBindCSSRVsHook.OriginalDisposeSafe(im, cs, data);
        PostImmediateBindSRVs(true, _d3dctx.PixelShader, im, (PixelShader*) cs, data);
    }

    private void PostImmediateBindSRVs(
        bool isPS,
        SDX11.CommonShaderStage stage,
        ImmediateContextEx* im,
        PixelShader* ps,
        ImmediateSRV* data
    )
    {
        Debug.Assert(im == Device.Instance()->ImmediateContext);

        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        // TODO: Identifying by the shader might be much more reliable long-term.
        if (ps->SamplerCount >= 1 && (data[0].Texture == rtm->GameplayTextureUnk1 || data[0].Texture == rtm->GameplayTextureUnk3))
        {
            // Service.PluginLog.Debug($"ImmediateBind{(isPS ? "PS" : "CS")}SRVs #{_drawCount} 0x{(long) (nint) ps}");
            var tex = data[0].Texture;

            SDX11.SamplerState? sampler = null;

            if (Service.Config._.Game.IsEnabled && Service.Config._.ResolutionScalingMode == ResolutionScalingMode.Point)
            {
                sampler = _samplerPoint;
            }
            else if (tex->MipLevel != 1)
            {
                sampler = _samplerMips;
                _d3dctx.GenerateMips(CppObject.FromPointer<SDX11.ShaderResourceView>((nint) tex->D3D11ShaderResourceView));
            }
            else if (Service.Config._.Game.IsEnabled && Service.Config._.ResolutionScalingMode == ResolutionScalingMode.Linear)
            {
                sampler = _samplerLinear;
            }

            if (sampler is not null)
            {
                stage.SetSampler(0, sampler);
                if (!isPS)
                {
                    _d3dctx.PixelShader.SetSampler(0, sampler);
                }
            }
        }
    }

    private int CreateTexture2DDetour(ID3D11Device* d3ddev, D3D11_TEXTURE2D_DESC* desc, D3D11_SUBRESOURCE_DATA* initialData, ID3D11Texture2D** tex)
    {
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        if (tex != &rtm->GameplayTextureUnk1->D3D11Texture2D && tex != &rtm->GameplayTextureUnk3->D3D11Texture2D)
        {
            return _createTexture2DHook.OriginalDisposeSafe(d3ddev, desc, initialData, tex);
        }

        if (rtm->GameplayTextureUnk1->MipLevel != 1)
        {
            desc->MiscFlags |= (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_GENERATE_MIPS;
        }

        return _createTexture2DHook.OriginalDisposeSafe(d3ddev, desc, initialData, tex);
    }

    private enum ForceUpdateRTMState
    {
        _0_Idle,
        _1_FakeInv,
        _2_ToScale
    }

    private readonly struct ScaleState : IDisposable
    {
        public readonly uint OrigWidth;
        public readonly uint OrigHeight;

        public ScaleState()
        {
            var dev = (DeviceEx*) Device.Instance();
            OrigWidth = dev->Width;
            OrigHeight = dev->Height;

            var scale = MathF.Max(1f, Service.Config._.Game.Scale);
            GetScaledWidthHeight(OrigWidth, OrigHeight, scale, out dev->Width, out dev->Height);
        }

        public readonly void Dispose()
        {
            var dev = (DeviceEx*) Device.Instance();
            dev->Width = OrigWidth;
            dev->Height = OrigHeight;
        }
    }
}
