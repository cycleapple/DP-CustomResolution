using Dalamud.Game.Config;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FloppyUtils;
using SharpDX;
using System;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using SDX11 = SharpDX.Direct3D11;

namespace CustomResolution;

public unsafe class GameSizeState : IDisposable
{
    private StringBuilder _dbg = new();
    private Hook<RTMApplyScaling> _rtmApplyScalingHook;
    private Hook<RTMDestroyAfterResize> _rtmDestroyAfterResizeHook;
    private Hook<RTMRegenAfterResize> _rtmRegenAfterResizeHook;
    private Hook<ImmediateProcessCommands> _immediateProcessCommandsHook;
    private Hook<DDX11PostTick> _ddx11PostTickHook;
    private Hook<TaskRenderGraphicsRender> _taskRenderGraphicsRenderHook;
    private Hook<PrepareTexture> _prepareTextureHook;
    private Hook<ImmediateBindPSSRVs> _immediateBindPSSRVsHook;
    private Hook<ImmediateBindCSSRVs> _immediateBindCSSRVsHook;
    private Hook<CreateTexture2D> _createTexture2DHook;

    private SDX11.DeviceContext _d3dctx;
    private SDX11.Device _d3ddev;
    private SDX11.SamplerState _samplerMips;
    private SDX11.SamplerState _samplerPoint;

    private bool _wasEnabled = false;
    private object _renderLock = new();

    private ForceUpdateRTMState _forceUpdateRTM = ForceUpdateRTMState._0_Idle;

    private uint _drawCount;

    public GameSizeState()
    {
        var dev = (DeviceEx*) Device.Instance();
        _d3dctx = CppObject.FromPointer<SDX11.DeviceContext>((nint) dev->D3D11DeviceContext);
        _d3ddev = _d3dctx.Device;

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

        /* Writes DynamicResolutionTargetHeight to size[1] near the end,
         * but if in (gpose || main menu), only scale < 1??
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
        _rtmDestroyAfterResizeHook = Service.GameInteropProvider.HookFromAddress<RTMDestroyAfterResize>(
            dev->RenderTargetManagerResizeDestroyCallback->Slots[0].Function,
            RTMDestroyAfterResizeDetour
        );
        _rtmDestroyAfterResizeHook.Enable();
        _rtmRegenAfterResizeHook = Service.GameInteropProvider.HookFromAddress<RTMRegenAfterResize>(
            dev->RenderTargetManagerResizeRegenCallback->Slots[0].Function,
            RTMRegenAfterResizeDetour
        );
        _rtmRegenAfterResizeHook.Enable();

        /* ImmediateContextDX11.ProcessCommands can run in either DeviceX11.PostTick,
         * or in RenderThread.Run, which is prone to race conditions and crashes with manual RTM regen.
         * While we can regen RTMs after ProcessCommands is done, it would still leave us
         * with some other race conditions regarding the fields we modify.
         * Let's wrap it in a C# lock and call it a day...
         */
        _immediateProcessCommandsHook = Service.GameInteropProvider.HookFromAddress<ImmediateProcessCommands>(
            Service.SigScanner.ScanText("48 89 5C 24 10 48 89 6C 24 18 56 57 41 56 48 83 EC 30 48 8B 01 41 8B F0 48 8B EA"),
            ImmediateProcessCommandsDetour
        );
        _immediateProcessCommandsHook.Enable();

        _ddx11PostTickHook = Service.GameInteropProvider.HookFromAddress<DDX11PostTick>(
            Service.SigScanner.ScanText("48 89 5C 24 10 48 89 6C 24 18 48 89 74 24 20 57 41 54 41 55 41 56 41 57 B8 30 43 00 00 ?? ?? ?? ?? ?? 48 2B E0 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? 00 00 8B 15"),
            DDX11PostTickDetour
        );
        _ddx11PostTickHook.Enable();

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
        Service.PluginLog.Debug($"TaskRenderGraphicsRender Task @ 0x{(long) taskRenderGraphicsRenderTask:X16} -> 0x{(long) (nint) taskRenderGraphicsRenderTask->Func:X16}");
        _taskRenderGraphicsRenderHook = Service.GameInteropProvider.HookFromAddress<TaskRenderGraphicsRender>(
            taskRenderGraphicsRenderTask->Func,
            TaskRenderGraphicsRenderDetour
        );
        _taskRenderGraphicsRenderHook.Enable();

        /* Texture preparation used in a lot of places, but most notable for
         * writing to the mip level count of the gameplay texture and also updating the D3D11 ptrs.
         */
        _prepareTextureHook = Service.GameInteropProvider.HookFromAddress<PrepareTexture>(
            Service.SigScanner.ScanText("40 53 48 83 EC 20 48 8B 02 48 8B D9 48 89 41 38 48 8B 02 48 89 41 40"),
            PrepareTextureDetour
        );
        _prepareTextureHook.Enable();

        /* The shader resource view of GameplayTexture gets read by these two.
         */
        _immediateBindPSSRVsHook = Service.GameInteropProvider.HookFromAddress<ImmediateBindPSSRVs>(
            Service.SigScanner.ScanText("48 8B C4 4C 89 40 18 48 89 48 08 53 56 48 83 EC 68 44 8B 5A 44 48 8D B1 E8 03 00 00 4C 89 68 D8"),
            ImmediateBindPSSRVsDetour
        );
        _immediateBindPSSRVsHook.Enable();
        _immediateBindCSSRVsHook = Service.GameInteropProvider.HookFromAddress<ImmediateBindCSSRVs>(
            Service.SigScanner.ScanText("48 8B C4 4C 89 40 18 48 89 48 08 53 56 48 83 EC 68 44 8B 5A 44 48 8D B1 E8 13 00 00 4C 89 68 D8"),
            ImmediateBindCSSRVsDetour
        );
        _immediateBindCSSRVsHook.Enable();

        /* And because all that isn't bad enough:
         * Hook ID3D11Device funcs based on their vtable indices, to allow mipmap generation,
         * and to allow changing the sampler state before drawing.
         */
        _createTexture2DHook = Service.GameInteropProvider.HookFromAddress<CreateTexture2D>(
            ((ID3D11Device*) _d3ddev.NativePointer)->lpVtbl[5],
            CreateTexture2DDetour
        );
        _createTexture2DHook.Enable();
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
    private delegate byte RTMDestroyAfterResize();

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void RTMRegenAfterResize();

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void ImmediateProcessCommands(ImmediateContext* ctx, RenderCommandBufferGroup* cmds, uint count);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void DDX11PostTick(DeviceEx* dev);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void TaskRenderGraphicsRender();

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate ulong PrepareTexture(Texture* tex, uint* size, byte mips, uint format);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void ImmediateBindPSSRVs(ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data);

    // [UnmanagedFunctionPointer(CallingConvention.FastCall)]
    private delegate void ImmediateBindCSSRVs(ImmediateContextEx* im, Shader* cs, ImmediateSRV* data);

    private delegate int CreateTexture2D(ID3D11Device* d3ddev, D3D11_TEXTURE2D_DESC* desc, D3D11_SUBRESOURCE_DATA* initialData, ID3D11Texture2D** tex);

    private delegate void Draw(ID3D11DeviceContext* d3dctx, uint vertexCount, uint startVertexLocation);

    public void Dispose()
    {
        _rtmApplyScalingHook.Dispose();
        _rtmDestroyAfterResizeHook.Dispose();
        _rtmRegenAfterResizeHook.Dispose();
        _immediateProcessCommandsHook.Dispose();
        _ddx11PostTickHook.Dispose();
        _taskRenderGraphicsRenderHook.Dispose();
        _prepareTextureHook.Dispose();
        _immediateBindPSSRVsHook.Dispose();
        _immediateBindCSSRVsHook.Dispose();
        _createTexture2DHook.Dispose();
        lock (_renderLock)
        {
            _samplerPoint.Dispose();
            _samplerMips.Dispose();
        }
        _d3ddev.Dispose();
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
            _dbg.Append(@$"DR !{gfx->DynamicRezoEnable} +{gfx->DynamicRezoEnableBeyond1} _{gfx->DynamicRezoEnableCutScene} ?{gfx->DynamicRezoEnableUnkx47} ?{gfx->DynamicRezoEnableUnkx48}
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
                gfx->GraphicsRezoUpscaleType = cfg.Scale <= 1f ? Service.Config._.ResolutionScalingMode.ToXIV() : ResolutionScalingMode.Linear.ToXIV();
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

        // Check if the backing RTs are the expected size.
        var tex = rtm->GameplayTextureUnk1;
        if (tex->AllocatedWidth != widthS || tex->AllocatedHeight != heightS)
        {
            if (!unloading)
            {
                lock (_renderLock)
                {
                    Service.PluginLog.Debug($"Regenerating dirty RTM - locking ImmediateContextDX11.ProcessCommands");
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
            Service.PluginLog.Debug("_forceUpdateRTM -> 1");
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
        heightS = (uint) MathF.Round(height * scale);
        widthS = (width * heightS) / height;
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
        gfx->DynamicRezoEnableBeyond1 = (byte) (cfg.Scale > 1f ? 1 : 0);

        Service.PluginLog.Debug($"Applying scaling, before: {size[0]} {size[1]} {unk1}");
        _rtmApplyScalingHook.OriginalDisposeSafe(rtm, size, unk1);
        Service.PluginLog.Debug($"Applying scaling, after: {size[0]} {size[1]} {unk1}");

        gfx->DynamicRezoEnableBeyond1 = _DynamicRezoEnableBeyond1;
    }

    private byte RTMDestroyAfterResizeDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
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
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
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

    private void ImmediateProcessCommandsDetour(ImmediateContext* ctx, RenderCommandBufferGroup* cmds, uint count)
    {
        _drawCount = 0;
        // Service.PluginLog.Debug("--- ImmediateProcessCommands ---");

        lock (_renderLock)
        {
            _immediateProcessCommandsHook.OriginalDisposeSafe(ctx, cmds, count);
        }
    }

    private void DDX11PostTickDetour(DeviceEx* dev)
    {
        _ddx11PostTickHook.OriginalDisposeSafe(dev);
    }

    private void TaskRenderGraphicsRenderDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (Service.Plugin.Unloading || !cfg.IsEnabled)
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

        if (!Service.Plugin.Unloading && cfg.Game.IsEnabled && cfg.Game.Scale > 2f)
        {
            mips = (byte) MathF.Ceiling(MathF.Log2(cfg.Game.Scale));
        }

        Service.PluginLog.Debug($"Preparing gameplay texture 0x{(long) (nint) tex:X16} with {mips} mips, currently 0x{(long) (nint) tex->D3D11Texture2D:X16}");
        var rv = _prepareTextureHook.OriginalDisposeSafe(tex, size, mips, format);
        Service.PluginLog.Debug($"After: 0x{rv:X16} 0x{(long) (nint) tex->D3D11Texture2D:X16} 0x{(long) (nint) tex->D3D11ShaderResourceView:X16}");
        return rv;
    }

    private void ImmediateBindPSCSSRVsDetourCommon(
        bool isPS,
        SDX11.CommonShaderStage stage,
        ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data
    )
    {
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        bool called = false;

        // TODO: Identifying by the shader might be much more reliable long-term.
        if (ps->Shader.SamplerCount >= 1 && (data[0].Texture == rtm->GameplayTextureUnk1 || data[0].Texture == rtm->GameplayTextureUnk3))
        {
            // Service.PluginLog.Debug($"ImmediateBind{(isPS ? "PS" : "CS")}SRVs #{_drawCount} 0x{(long) (nint) ps}");
            var tex = data[0].Texture;

            Orig();

            if (Service.Config._.Game.IsEnabled && Service.Config._.ResolutionScalingMode == ResolutionScalingMode.Point)
            {
                stage.SetSampler(0, _samplerPoint);
                if (!isPS)
                {
                    _d3dctx.PixelShader.SetSampler(0, _samplerPoint);
                }
            }
            else if (tex->MipLevel != 1)
            {
                _d3dctx.PixelShader.SetSampler(0, _samplerMips);
                if (!isPS)
                {
                    _d3dctx.PixelShader.SetSampler(0, _samplerMips);
                }
                _d3dctx.GenerateMips(CppObject.FromPointer<SDX11.ShaderResourceView>((nint) tex->D3D11ShaderResourceView));
            }
        }

        Orig();

        void Orig()
        {
            if (called)
            {
                return;
            }

            called = true;
            _drawCount++;

            if (isPS)
            {
                _immediateBindPSSRVsHook.OriginalDisposeSafe(im, ps, data);
            }
            else
            {
                _immediateBindCSSRVsHook.OriginalDisposeSafe(im, (Shader*) ps, data);
            }
        }
    }

    private void ImmediateBindPSSRVsDetour(ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data)
    {
        ImmediateBindPSCSSRVsDetourCommon(true, _d3dctx.PixelShader, im, ps, data);
    }

    private void ImmediateBindCSSRVsDetour(ImmediateContextEx* im, Shader* cs, ImmediateSRV* data)
    {
        ImmediateBindPSCSSRVsDetourCommon(false, _d3dctx.ComputeShader, im, (PixelShader*) cs, data);
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

    private struct ScaleState : IDisposable
    {
        public uint Width;
        public uint Height;

        public ScaleState()
        {
            var dev = (DeviceEx*) Device.Instance();
            Width = dev->Width;
            Height = dev->Height;

            var scale = MathF.Max(1f, Service.Config._.Game.Scale);
            GetScaledWidthHeight(Width, Height, scale, out dev->Width, out dev->Height);
        }

        public void Dispose()
        {
            var dev = (DeviceEx*) Device.Instance();
            dev->Width = Width;
            dev->Height = Height;
        }
    }
}


[StructLayout(LayoutKind.Explicit)]
public unsafe struct GraphicsConfigEx
{
    [FieldOffset(0)]
    public GraphicsConfig _;

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

    // Totle screen: Gets blitted FROM after actual gameplay texture. Actual purpose unknown.
    // Im-game outdoors: Gets blitted to backbuffer.
    [FieldOffset(0x68)]
    public Texture* GameplayTextureUnk1;

    // Gets blitted TO after actual gameplay texture, using Unk1. Actual purpose unknown.
    [FieldOffset(0x100)]
    public Texture* GameplayTextureUnk2;

    // Title screen: Gets blitted to backbuffer.
    // In-game outdoors: Totally unknown.
    [FieldOffset(0x258)]
    public Texture* GameplayTextureUnk3;

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

[StructLayout(LayoutKind.Explicit, Size = 0x18)]
public unsafe struct ImmediateSRV
{
    // D3D11ShaderResourceView must be at 0x58 + (GlobalIndex % 3) * 8
    // Smells like something swapchain-adjacent, but have yet to analyze anything with != null.
    [FieldOffset(0x0)]
    public nint Unkx0;

    [FieldOffset(0x8)]
    public Texture* Texture;

    [FieldOffset(0x10)]
    public uint Unkx10;

    [FieldOffset(0x14)]
    public uint Unkx14;
}
