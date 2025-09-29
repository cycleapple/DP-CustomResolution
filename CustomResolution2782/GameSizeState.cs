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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using Device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using SDX11 = SharpDX.Direct3D11;

namespace CustomResolution;

public unsafe sealed class GameSizeState : GameDrawHooks
{
    private readonly StringBuilder _dbg = new();

    private readonly SDX11.SamplerState _samplerMips;
    private readonly SDX11.SamplerState _samplerPoint;
    private readonly SDX11.SamplerState _samplerLinear;

    private bool _wasEnabled = false;
    private object _renderLock = new();

    private ForceUpdateRTMState _forceUpdateRTM = ForceUpdateRTMState._0_Idle;

    private uint _drawCount;

    private ResolutionScalingMode? _setConfigGraphicsRezoType;

    public GameSizeState()
    {
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

        Enable();
    }

    public bool ConfigDynRezo { get; private set; }
    public ResolutionScalingMode ConfigGraphicsRezoType { get; private set; }
    public float ConfigGraphicsRezoScale { get; private set; }

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }

    public string DebugInfo => _dbg.ToString();

    protected override void DisposeD3D(bool disposing)
    {
        lock (_renderLock)
        {
            _samplerLinear.Dispose();
            _samplerPoint.Dispose();
            _samplerMips.Dispose();
        }

        base.DisposeD3D(disposing);
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

    protected override void RTMApplyScalingDetour(RenderTargetManagerEx* rtm, uint* size, byte unk1)
    {
        Debug.Assert(rtm == RenderTargetManager.Instance());

        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            base.RTMApplyScalingDetour(rtm, size, unk1);
            return;
        }

        var gfx = (GraphicsConfigEx*) GraphicsConfig.Instance();
        var _DynamicRezoEnableBeyond1 = gfx->DynamicRezoEnableBeyond1;
        gfx->DynamicRezoEnableBeyond1 = (byte) (cfg.Scale > 1f ? 1 : 0);

        Service.PluginLog.Debug($"Applying scaling, before: {size[0]} {size[1]} {unk1}");
        base.RTMApplyScalingDetour(rtm, size, unk1);
        Service.PluginLog.Debug($"Applying scaling, after: {size[0]} {size[1]} {unk1}");

        gfx->DynamicRezoEnableBeyond1 = _DynamicRezoEnableBeyond1;
    }

    protected override byte RTMDestroyAfterResizeDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            return base.RTMDestroyAfterResizeDetour();
        }

        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        Service.PluginLog.Debug($"Destroying RTM resources");
        byte rv = base.RTMDestroyAfterResizeDetour();
        Service.PluginLog.Debug($"After: {rv} 0x{(long) (nint) rtm->GameplayTextureUnk1->D3D11Texture2D:X16} 0x{(long) (nint) rtm->GameplayTextureUnk3->D3D11Texture2D:X16}");
        return rv;
    }

    protected override void RTMRegenAfterResizeDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            base.RTMRegenAfterResizeDetour();
            return;
        }

        var dev = (DeviceEx*) Device.Instance();
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        using var scale = new ScaleState();
        Service.PluginLog.Debug($"Regenerating RTM resources: {dev->Width} x {dev->Height}");
        base.RTMRegenAfterResizeDetour();
        Service.PluginLog.Debug($"After: 0x{(long) (nint) rtm->GameplayTextureUnk1->D3D11Texture2D:X16} 0x{(long) (nint) rtm->GameplayTextureUnk3->D3D11Texture2D:X16}");
    }

    protected override void ImmediateProcessCommandsDetour(ImmediateContextEx* ctx, RenderCommandBufferGroup* cmds, uint count)
    {
        _drawCount = 0;
        // Service.PluginLog.Debug("--- ImmediateProcessCommands ---");

        lock (_renderLock)
        {
            base.ImmediateProcessCommandsDetour(ctx, cmds, count);
        }
    }

    protected override void TaskRenderGraphicsRenderDetour()
    {
        ref var cfg = ref Service.Config._.Game;
        if (FService.Unloading || !cfg.IsEnabled)
        {
            base.TaskRenderGraphicsRenderDetour();
            return;
        }

        lock (_renderLock)
        {
            using var scale = new ScaleState();
            base.TaskRenderGraphicsRenderDetour();
        }
    }

    protected override ulong PrepareTextureDetour(Texture* tex, uint* size, byte mips, uint format)
    {
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        if (tex != rtm->GameplayTextureUnk1 && tex != rtm->GameplayTextureUnk3)
        {
            return base.PrepareTextureDetour(tex, size, mips, format);
        }

        ref var cfg = ref Service.Config._;

        if (!FService.Unloading && cfg.Game.IsEnabled && cfg.Game.Scale > 2f)
        {
            mips = (byte) MathF.Ceiling(MathF.Log2(cfg.Game.Scale));
        }

        Service.PluginLog.Debug($"Preparing gameplay texture 0x{(long) (nint) tex:X16} with {mips} mips, currently 0x{(long) (nint) tex->D3D11Texture2D:X16}");
        var rv = base.PrepareTextureDetour(tex, size, mips, format);
        Service.PluginLog.Debug($"After: 0x{rv:X16} 0x{(long) (nint) tex->D3D11Texture2D:X16} 0x{(long) (nint) tex->D3D11ShaderResourceView:X16}");
        return rv;
    }

    protected override void ImmediateBindPSCSSRVsDetourCommon(
        bool isPS,
        SDX11.CommonShaderStage stage,
        ImmediateContextEx* im, PixelShader* ps, ImmediateSRV* data
    ) {
        Debug.Assert(im == Device.Instance()->ImmediateContext);

        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        bool called = false;

        // TODO: Identifying by the shader might be much more reliable long-term.
        if (ps->SamplerCount >= 1 && (data[0].Texture == rtm->GameplayTextureUnk1 || data[0].Texture == rtm->GameplayTextureUnk3))
        {
            // Service.PluginLog.Debug($"ImmediateBind{(isPS ? "PS" : "CS")}SRVs #{_drawCount} 0x{(long) (nint) ps}");
            var tex = data[0].Texture;

            Orig();

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

        Orig();

        void Orig()
        {
            if (called)
            {
                return;
            }

            called = true;
            _drawCount++;

            base.ImmediateBindPSCSSRVsDetourCommon(isPS, stage, im, ps, data);
        }
    }

    protected override int CreateTexture2DDetour(ID3D11Device* d3ddev, D3D11_TEXTURE2D_DESC* desc, D3D11_SUBRESOURCE_DATA* initialData, ID3D11Texture2D** tex)
    {
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();
        if (tex != &rtm->GameplayTextureUnk1->D3D11Texture2D && tex != &rtm->GameplayTextureUnk3->D3D11Texture2D)
        {
            return base.CreateTexture2DDetour(d3ddev, desc, initialData, tex);
        }

        if (rtm->GameplayTextureUnk1->MipLevel != 1)
        {
            desc->MiscFlags |= (uint) D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_GENERATE_MIPS;
        }

        return base.CreateTexture2DDetour(d3ddev, desc, initialData, tex);
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
