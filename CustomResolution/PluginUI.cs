using CustomResolution.Windows;
using Dalamud.Interface.Windowing;
using System;

namespace CustomResolution;

public sealed class PluginUI : IDisposable
{
    private ConfigWindow _configWindow;

    internal PluginUI()
    {
        Service.PluginInterface.UiBuilder.Draw += Draw;
        Service.PluginInterface.UiBuilder.OpenConfigUi += ShowConfigWindow;

        WindowSystem.AddWindow(_configWindow = new ConfigWindow());
    }

    public WindowSystem WindowSystem { get; } = new("CustomResolution");

    public bool SettingsVisible
    {
        get => _configWindow.IsOpen;
        set
        {
            _configWindow.IsOpen = value;
            _configWindow.UpdateFromConfig();
        }
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        _configWindow.Dispose();

        Service.PluginInterface.UiBuilder.Draw -= Draw;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= ShowConfigWindow;
    }

    public void Draw()
    {
        WindowSystem.Draw();
    }

    private void ShowConfigWindow()
    {
        SettingsVisible = true;
    }
}
