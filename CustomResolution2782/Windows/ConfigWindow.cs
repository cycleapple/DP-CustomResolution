using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;

namespace CustomResolution.Windows;

public class ConfigWindow : Window, IDisposable
{
    private VirtualKey[] _validKeys = new VirtualKey[] { VirtualKey.NO_KEY }.Union(Service.KeyState.GetValidVirtualKeys()).ToArray();

    private int[] _displayCurrentWH = new int[2];
    private int[] _displayCurrentWindowWH = new int[2];

    private bool _configIsEnabled;
    private VirtualKey _configHotkeyKey;
    private ModifierKey _configHotkeyModifier;
    private bool _configIsScale;
    private float _configScale;
    private int[] _configWH = new int[2];
    private DXVKDWMHackMode _configDXVKDWMHackMode;

    public ConfigWindow() : base(
        "CustomResolution",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        UpdateFromConfig();
    }

    public void Dispose() {
    }

    public void UpdateFromConfig()
    {
        var config = Service.Config;

        _configIsEnabled = config.IsEnabled;
        _configHotkeyKey = config.HotkeyKey;
        _configHotkeyModifier = config.HotkeyModifier;
        _configIsScale = config.IsScale;
        _configScale = config.Scale;
        _configWH[0] = (int) config.Width;
        _configWH[1] = (int) config.Height;
        _configDXVKDWMHackMode = config.DXVKDWMHackMode;
    }

    public void UpdateToConfig()
    {
        var config = Service.Config;

        config.IsEnabled = _configIsEnabled;
        config.HotkeyKey = _configHotkeyKey;
        config.HotkeyModifier = _configHotkeyModifier;
        config.IsScale = _configIsScale;
        config.Scale = _configScale;
        config.Width = (uint) _configWH[0];
        config.Height = (uint) _configWH[1];
        config.DXVKDWMHackMode = _configDXVKDWMHackMode;

        config.Save();
    }

    public override void Draw()
    {
        var plugin = Service.Plugin;
        var save = false;

        _displayCurrentWH[0] = (int) plugin.CurrentWidth;
        _displayCurrentWH[1] = (int) plugin.CurrentHeight;
        _displayCurrentWindowWH[0] = (int) plugin.CurrentWindowWidth;
        _displayCurrentWindowWH[1] = (int) plugin.CurrentWindowHeight;

        ImGui.BeginDisabled();
        ImGui.InputInt2("Current window size", ref _displayCurrentWindowWH[0]);
        ImGui.InputInt2("Current render size", ref _displayCurrentWH[0]);
        ImGui.EndDisabled();

        ImGui.Checkbox("Enabled", ref _configIsEnabled);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.BeginCombo("##_configHotkeyModifier", _configHotkeyModifier.ToHumanNameString()))
        {
            foreach (var key in Enum.GetValues<ModifierKey>())
            {
                if (ImGui.Selectable(key.ToHumanNameString(), _configHotkeyModifier == key, ImGuiSelectableFlags.None))
                {
                    _configHotkeyModifier = key;
                }

                if (ImGui.IsItemHovered() && key.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo("##_configHotkeyKey", _configHotkeyKey.ToHumanNameString()))
        {
            foreach (var key in _validKeys)
            {
                if (ImGui.Selectable(key.ToHumanNameString(), _configHotkeyKey == key, ImGuiSelectableFlags.None))
                {
                    _configHotkeyKey = key;
                }

                if (ImGui.IsItemHovered() && key.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Text("Hotkey");


        if (!_configIsEnabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.Checkbox("Use scale", ref _configIsScale);

        if (_configIsScale)
        {
            if (ImGui.InputFloat("Scale", ref _configScale, 0.01f, 0.1f, "%.3f", ImGuiInputTextFlags.EnterReturnsTrue))
            {
                save = true;
            }
        }
        else
        {
            ImGui.InputInt2("Size in pixels", ref _configWH[0]);
        }

        if (!_configIsEnabled)
        {
            ImGui.EndDisabled();
        }

        if (ImGui.BeginCombo("Borderless window workaround", _configDXVKDWMHackMode.ToHumanNameString()))
        {
            foreach (var mode in Enum.GetValues<DXVKDWMHackMode>())
            {
                if (ImGui.Selectable(mode.ToHumanNameString(), _configDXVKDWMHackMode == mode, ImGuiSelectableFlags.None))
                {
                    _configDXVKDWMHackMode = mode;
                }

                if (ImGui.IsItemHovered() && mode.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"Fixes DXVK borderless window causing black screens when alt-tabbing.
This can *possibly* impact performance, depending on your Windows version and GPU.

Feel free to experiment with this toggle.
Make sure to switch the game to windowed and then back to borderless windowed when changing.

Works even with the scaling above disabled.

Not intended to be used with proper fullscreen.");
        }

        if (ImGui.Button("Save and apply"))
        {
            save = true;
        }

        if (save)
        {
            UpdateToConfig();
        }
    }
}
