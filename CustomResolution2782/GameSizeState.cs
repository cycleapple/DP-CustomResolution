using CustomResolution.Hooks;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Config;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public unsafe class GameSizeState : IDisposable
{
    private bool _wasEnabled = false;

    private bool? _forceDynRezoConfig;

    public bool ConfigDynRezo { get; private set; }
    public ResolutionScalingMode ConfigGraphicsRezoType { get; private set; }
    public float ConfigGraphicsRezoScale { get; private set; }

    public void Dispose()
    {
        Service.Framework.RunOnFrameworkThread(Update);
    }

    public void ForceDynRezoConfig(bool value)
    {
        _forceDynRezoConfig = value;
    }

    public void Update()
    {
        ref var cfg = ref Service.Config._.Game;

        if (_forceDynRezoConfig.HasValue)
        {
            Service.GameConfig.System.Set(SystemConfigOption.DynamicRezoType.ToString(), _forceDynRezoConfig.Value ? 1U : 0U);
            _forceDynRezoConfig = null;
        }

        ConfigDynRezo = Service.GameConfig.System.GetUInt(SystemConfigOption.DynamicRezoType.ToString()) != 0U;
        // GameConfig starts with FSR at 0; GraphicsConfig starts with FSR at 1
        ConfigGraphicsRezoType = (ResolutionScalingMode) (Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoUpscaleType.ToString()) + 1);
        ConfigGraphicsRezoScale = Service.GameConfig.System.GetUInt(SystemConfigOption.GraphicsRezoScale.ToString()) / 100f;

        var dev = Device.Instance();
        var gfx = GraphicsConfig.Instance();
        var rtm = (RenderTargetManagerEx*) RenderTargetManager.Instance();

        //Service.PluginLog.Debug($"GraphicsRezoScale 0x{(long) (IntPtr) (&gfx->GraphicsRezoScale):X16}");
        //Service.PluginLog.Debug($"RTM 0x{(long) (IntPtr) rtm:X16}");

        bool unloading = Service.Plugin.Unloading;
        bool enabled = !unloading && cfg.IsEnabled;

        if (enabled || _wasEnabled)
        {
            gfx->GraphicsRezoScale = enabled ? cfg.Scale : ConfigGraphicsRezoScale;
            gfx->DynamicRezoEnable = (byte) (ConfigDynRezo || enabled ? 1 : 0);
            if (enabled)
            {
                gfx->GraphicsRezoUpscaleType = (byte) (cfg.Scale <= 1f ? Service.Config._.ResolutionScalingMode : ResolutionScalingMode.Fast);
            }
            else
            {
                gfx->GraphicsRezoUpscaleType = (byte) ConfigGraphicsRezoType;
            }
            _wasEnabled = enabled;
        }

        if (enabled)
        {
            rtm->DynamicResolutionMinimumHeight = rtm->DynamicResolutionMaximumHeight;
        }

        /*
        rtm->Resolution_Width = (ushort) (dev->Width * gfx->GraphicsRezoScale);
        rtm->Resolution_Height = (ushort) (dev->Height * gfx->GraphicsRezoScale);
        rtm->DynamicResolutionActualTargetHeight = (ushort) (dev->Height * gfx->GraphicsRezoScale);
        rtm->DynamicResolutionTargetHeight = (ushort) (dev->Height * gfx->GraphicsRezoScale);
        rtm->DynamicResolutionMaximumHeight = (ushort) (dev->Height * gfx->GraphicsRezoScale);
        rtm->DynamicResolutionMinimumHeight = (ushort) (dev->Height * gfx->GraphicsRezoScale);
        */

#if false
        Service.PluginLog.Debug($"DR {gfx->DynamicRezoEnable}");
        Service.PluginLog.Debug($"RTM {rtm->Resolution_Width} x {rtm->Resolution_Height}");
        Service.PluginLog.Debug($"RTM H {rtm->DynamicResolutionActualTargetHeight} {rtm->DynamicResolutionTargetHeight} {rtm->DynamicResolutionMaximumHeight} {rtm->DynamicResolutionMinimumHeight}");
        Service.PluginLog.Debug($"RTM S {rtm->GraphicsRezoScales[0]} {rtm->GraphicsRezoScales[1]} {rtm->GraphicsRezoScales[2]} {rtm->GraphicsRezoScales[3]} {rtm->GraphicsRezoScales[4]}");
#endif
    }

    // RenderTargetManager is updated very sporadically, and some fields we need flew out
    // https://github.com/aers/FFXIVClientStructs/commit/589df2aa5cd9c98b4d62269034cd6da903f49b5f#diff-8e7d9b03cb91cb07a8d7b463b5be4672793a328703bde393e7acd890822a72cf
    [StructLayout(LayoutKind.Explicit)]
    private struct RenderTargetManagerEx
    {
        [FieldOffset(0)]
        public RenderTargetManager _;

#if CRES_CLEAN || false
        public ref uint Resolution_Width => ref _.Resolution_Width;
        public ref uint Resolution_Height => ref _.Resolution_Height;
        public ref ushort DynamicResolutionActualTargetHeight => ref _.DynamicResolutionActualTargetHeight;
        public ref ushort DynamicResolutionTargetHeight => ref _.DynamicResolutionTargetHeight;
        public ref ushort DynamicResolutionMaximumHeight => ref _.DynamicResolutionMaximumHeight;
        public ref ushort DynamicResolutionMinimumHeight => ref _.DynamicResolutionMinimumHeight;
        public ref float GraphicsRezoScale => ref _.GraphicsRezoScale;
#else
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

        public const int GraphicsRezoScalesCount = 5;
        [FieldOffset(0x70C)]
        public fixed float GraphicsRezoScales[GraphicsRezoScalesCount];
        [FieldOffset(0x70C + 4 * 0)]
        public float GraphicsRezoScalePrev;
        [FieldOffset(0x70C + 4 * 1)]
        public float GraphicsRezoScaleGlassWidth;
        [FieldOffset(0x70C + 4 * 2)]
        public float GraphicsRezoScaleGlassHeight;
        [FieldOffset(0x70C + 4 * 3)]
        public float GraphicsRezoScaleUnk1;
        [FieldOffset(0x70C + 4 * 4)]
        public float GraphicsRezoScaleUnk2;
#endif
    }
}
