using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using Newtonsoft.Json;
using System;

namespace CustomResolution;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public ConfigurationV1 V1 = new();
    public int Version { get; set; } = 1;

    [JsonIgnore]
    public ref ConfigurationV1 _ => ref V1;

    [Obsolete]
    public bool IsEnabled = true;
    [Obsolete]
    public bool IsScale = true;
    [Obsolete]
    public float Scale = 1f;
    [Obsolete]
    public uint Width = 1024;
    [Obsolete]
    public uint Height = 1024;
    [Obsolete]
    public VirtualKey HotkeyKey = VirtualKey.NO_KEY;
    [Obsolete]
    public ModifierKey HotkeyModifier = ModifierKey.NONE;
    [Obsolete]
    public DXVKDWMHackMode DXVKDWMHackMode = DXVKDWMHackMode.Off;
    [Obsolete]
    public MinSizeMode MinSizeMode = MinSizeMode.Unchanged;

    [NonSerialized]
    private IDalamudPluginInterface pluginInterface = null!;

    internal void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
        Upgrade();
    }

    public void Save()
    {
        _.Display.Clamp();
        _.Game.Clamp();
        pluginInterface.SavePluginConfig(this);
        Service.PluginUI.UpdateFromConfig();
    }

    private void Upgrade()
    {
#pragma warning disable CS0612 // Type or member is obsolete, but we want to migrate from it.
#pragma warning disable CS0618 // Type or member is obsolete, but we want to migrate from it.
        if (Version == 0)
        {
            Service.PluginLog.Info("Migrating config V0 to V1");
            V1 = new();
            // V1.Display.IsEnabled = IsEnabled; // Was enabled by default before, but let's disable.
            V1.Display.IsScale = IsScale;
            V1.Display.Scale = Scale;
            V1.Display.Width = Width;
            V1.Display.Height = Height;
            V1.Display.HotkeyKey = HotkeyKey;
            V1.Display.HotkeyModifier = HotkeyModifier;
            V1.DXVKDWMHackMode = DXVKDWMHackMode;
            V1.MinSizeMode = MinSizeMode;
            V1.Display.Clamp();
            Version++;
        }
#pragma warning restore CS0612
#pragma warning restore CS0618
    }
}
