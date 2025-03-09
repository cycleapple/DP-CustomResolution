using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CustomResolution;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsEnabled = true;
    public bool IsScale = true;
    public float Scale = 1f;
    public uint Width = 1024;
    public uint Height = 1024;

    public VirtualKey HotkeyKey = VirtualKey.NO_KEY;
    public ModifierKey HotkeyModifier = ModifierKey.NONE;

    public DXVKDWMHackMode DXVKDWMHackMode = DXVKDWMHackMode.Off;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    internal void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface!.SavePluginConfig(this);
    }
}

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
