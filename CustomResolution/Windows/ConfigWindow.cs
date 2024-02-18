using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Numerics;

namespace CustomResolution.Windows;

public class ConfigWindow : Window, IDisposable
{
    private int[] _displayCurrentWH = new int[2];
    private int[] _displayCurrentWindowWH = new int[2];

    private bool _configIsEnabled;
    private bool _configIsScale;
    private float _configScale;
    private int[] _configWH = new int[2];

    public ConfigWindow() : base(
        "CustomResolution",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(430, 184);
        SizeCondition = ImGuiCond.Always;

        UpdateFromConfig();
    }

    public void Dispose() {
    }

    public void UpdateFromConfig()
    {
        var config = Service.Config;

        _configIsEnabled = config.IsEnabled;
        _configIsScale = config.IsScale;
        _configScale = config.Scale;
        _configWH[0] = (int) config.Width;
        _configWH[1] = (int) config.Height;
    }

    public void UpdateToConfig()
    {
        var config = Service.Config;

        config.IsEnabled = _configIsEnabled;
        config.IsScale = _configIsScale;
        config.Scale = _configScale;
        config.Width = (uint) _configWH[0];
        config.Height = (uint) _configWH[1];

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
