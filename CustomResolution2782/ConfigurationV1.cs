using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Plugin;
using System;

namespace CustomResolution;

[Serializable]
public struct ConfigurationV1
{
    public SizeConfig Display = new();
    public SizeConfig Game = new();

    public ResolutionScalingMode ResolutionScalingMode = ResolutionScalingMode.FSR;
    public DXVKDWMHackMode DXVKDWMHackMode = DXVKDWMHackMode.Off;
    public MinSizeMode MinSizeMode = MinSizeMode.Unchanged;

    public ConfigurationV1()
    {
    }

    [Serializable]
    public struct SizeConfig
    {
        public bool IsEnabled = false;
        public bool IsScale = true;
        public float Scale = 1f;
        [NonSerialized]
        public unsafe fixed int WH[2];
        public unsafe uint Width
        {
            get => (uint) WH[0];
            set => WH[0] = (int) value;
        }
        public unsafe uint Height
        {
            get => (uint) WH[1];
            set => WH[1] = (int) value;
        }

        public VirtualKey HotkeyKey = VirtualKey.NO_KEY;
        public ModifierKey HotkeyModifier = ModifierKey.NONE;

        public SizeConfig()
        {
            Width = 1024;
            Height = 1024;
        }

        public void Clamp()
        {
            if (Width < 256)
            {
                Width = 256;
            }
            if (Height < 256)
            {
                Height = 256;
            }
            if (Scale < 0.001f)
            {
                Scale = 0.001f;
            }
        }
    }
}
