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

public enum CullingMode
{
    None,
    OnlyInFront,
    OnlyInView
}

public enum DutyMode
{
    Always,
    OutsideContent,
    InContent
}
