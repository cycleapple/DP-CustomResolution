using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

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

    public DXVKDWMHackMode DXVKDWMHackMode = DXVKDWMHackMode.Off;

    [NonSerialized]
    private DalamudPluginInterface? pluginInterface;

    internal void Initialize(DalamudPluginInterface pluginInterface)
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
    SetClientEdge = 2
}

public static class DXVKDWMHackModeExt
{
    public static string ToHumanNameString(this DXVKDWMHackMode mode) => mode switch
    {
        DXVKDWMHackMode.Off => "Off",
        DXVKDWMHackMode.UnsetPopup => "Best case: Disable WS_POPUP",
        DXVKDWMHackMode.SetClientEdge => "Worst case: Enable WS_EX_CLIENTEDGE",
        _ => mode.ToString(),
    };

    public static string? ToHumanInfoString(this DXVKDWMHackMode mode) => mode switch
    {
        DXVKDWMHackMode.UnsetPopup => "Works best with NVIDIA GPUs.",
        DXVKDWMHackMode.SetClientEdge => "Adds a 2 pixel border around the game.",
        _ => null
    };
}
