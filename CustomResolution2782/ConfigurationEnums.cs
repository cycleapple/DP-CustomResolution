using Dalamud.Game.ClientState.Keys;
using System.Collections.Generic;
using System.Reflection;

namespace CustomResolution;


// Must explicitly map to integer values, blame Newtonsoft.JSON
public enum DXVKDWMHackMode
{
    Off = 0,
    UnsetPopup = 1,
    SetClientEdgeResize = 3,
    SetClientEdgeBorder = 2,
}

public static class DXVKDWMHackModeExt
{
    public static string ToHumanNameString(this DXVKDWMHackMode mode) => mode switch
    {
        DXVKDWMHackMode.Off => "Off",
        DXVKDWMHackMode.UnsetPopup => "Best: -WS_POPUP",
        DXVKDWMHackMode.SetClientEdgeResize => "+WS_EX_CLIENTEDGE (resize)",
        DXVKDWMHackMode.SetClientEdgeBorder => "+WS_EX_CLIENTEDGE (border)",
        _ => mode.ToString(),
    };

    public static string? ToHumanInfoString(this DXVKDWMHackMode mode) => mode switch
    {
        DXVKDWMHackMode.UnsetPopup => "Least intrusive option, try this first.\nWorks best with NVIDIA GPUs.",
        DXVKDWMHackMode.SetClientEdgeResize => "Extends the game window 1 pixel to the bottom.\nDon't use if it makes text look blurry!",
        DXVKDWMHackMode.SetClientEdgeBorder => "Adds a 1 pixel border around the game.",
        _ => null
    };

    public static bool IsUnsetPopup(this DXVKDWMHackMode mode) => mode == DXVKDWMHackMode.UnsetPopup;

    public static bool IsSetClientEdge(this DXVKDWMHackMode mode) => mode switch
    {
        DXVKDWMHackMode.SetClientEdgeResize => true,
        DXVKDWMHackMode.SetClientEdgeBorder => true,
        _ => false
    };
}

public enum ModifierKey
{
    NONE = 0,
    CTRL = 1,
    ALT = 2,
    SHIFT = 4
}

public static class VirtualKeyExt
{
    private static readonly Dictionary<VirtualKey, string> _strings = new();

    static VirtualKeyExt()
    {
        foreach (var field in typeof(VirtualKey).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var key = (VirtualKey) field.GetValue(null)!;
            _strings[key] = key.GetFancyName();
        }
    }

    public static string ToHumanNameString(this VirtualKey mode)
    {
        return _strings[mode];
    }

    public static string? ToHumanInfoString(this VirtualKey mode) => mode switch
    {
        _ => null
    };
}

public static class ModifierKeyExt
{
    public static string ToHumanNameString(this ModifierKey mode) => mode switch
    {
        ModifierKey.NONE => "None",
        ModifierKey.CTRL => "Ctrl",
        ModifierKey.ALT => "Alt",
        ModifierKey.SHIFT => "Shift",
        _ => mode.ToString(),
    };

    public static string? ToHumanInfoString(this ModifierKey mode) => mode switch
    {
        _ => null
    };
}

public enum MinSizeMode
{
    Unchanged = 0,
    Unlocked = 1
}

public static class MinSizeModeExt
{
    public static string ToHumanNameString(this MinSizeMode mode) => mode switch
    {
        MinSizeMode.Unchanged => "Unchanged (1024x768)",
        MinSizeMode.Unlocked => "Unlocked",
        _ => mode.ToString(),
    };

    public static string? ToHumanInfoString(this MinSizeMode mode) => mode switch
    {
        MinSizeMode.Unchanged => "The game normally doesn't allow resizing to smaller than 1024x768.",
        MinSizeMode.Unlocked => "Allow resizing the game in windowed mode to smaller than 1024x768. Works best with scaling over 100%.",
        _ => null
    };
}

public enum ResolutionScalingMode
{
    Point = 0,
    Linear = 1,
    FSR = 2,
    DLSS = 3
}

public static class ResolutionScalingModeExt
{
    public static ResolutionScalingMode FromXIVSysConf(uint mode) => mode switch
    {
        0 => ResolutionScalingMode.FSR,
        1 => ResolutionScalingMode.DLSS,
        _ => ResolutionScalingMode.Linear
    };

    public static uint ToXIVSysConf(this ResolutionScalingMode mode) => mode switch
    {
        ResolutionScalingMode.FSR => 0,
        ResolutionScalingMode.DLSS => 1,
        _ => 0
    };

    public static ResolutionScalingMode FromXIVGFX(byte mode) => mode switch
    {
        0 => ResolutionScalingMode.Linear,
        1 => ResolutionScalingMode.FSR,
        2 => ResolutionScalingMode.DLSS,
        _ => ResolutionScalingMode.Linear
    };

    public static byte ToXIVGFX(this ResolutionScalingMode mode) => mode switch
    {
        ResolutionScalingMode.Point => 0,
        ResolutionScalingMode.Linear => 0,
        ResolutionScalingMode.FSR => 1,
        ResolutionScalingMode.DLSS => 2,
        _ => 0
    };

    public static string ToHumanNameString(this ResolutionScalingMode mode) => mode switch
    {
        ResolutionScalingMode.Point => "Pixelated",
        ResolutionScalingMode.Linear => "Blurry",
        ResolutionScalingMode.FSR => "AMD FSR",
        ResolutionScalingMode.DLSS => "NVIDIA DLSS",
        _ => mode.ToString(),
    };

    public static string? ToHumanInfoString(this ResolutionScalingMode mode) => mode switch
    {
        ResolutionScalingMode.Point => "Lowest quality option which results in pixelation.",
        ResolutionScalingMode.Linear => "Low quality option which results in blur.",
        ResolutionScalingMode.FSR => "Upscaling which works on any GPU for low performance overhead.",
        ResolutionScalingMode.DLSS => "DLSS in FFXIV is buggy, even without any plugins.",
        _ => null
    };

    public static bool IsBroken(this ResolutionScalingMode mode) => mode switch
    {
#if !DEBUG
        ResolutionScalingMode.DLSS => true,
#endif
        _ => false
    };

    public static unsafe bool IsSupported(this ResolutionScalingMode mode) => mode switch
    {
        ResolutionScalingMode.DLSS => PostEffectManagerEx.Instance()->DLSS != null,
        _ => true,
    };

    public static ResolutionScalingMode ToSupported(this ResolutionScalingMode mode) =>
        !mode.IsSupported() ? ResolutionScalingMode.FSR : mode;
}
