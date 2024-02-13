using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace CustomResolution;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private readonly List<Cmd> _cmds;
    private bool _unloading = false;
    private int _tickCount = 0;

    public Plugin([RequiredVersion("1.0")] DalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        Service.Plugin = this;

        Service.Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new();
        Service.Config.Initialize(Service.PluginInterface);

        Service.PluginUI = new();

        _cmds = typeof(Plugin).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Cmd).IsAssignableFrom(t))
            .Select(t => (Cmd) Activator.CreateInstance(t)!)
            .ToList();

        foreach (Cmd cmd in _cmds)
        {
            cmd.Register(Service.CommandManager);
        }

        Service.Framework.Update += OnFrameworkUpdate;
    }

    public string Name => "CustomResolution";

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }
    public uint CurrentWindowWidth { get; private set; }
    public uint CurrentWindowHeight { get; private set; }

    public void Dispose()
    {
        _tickCount = 0;
        _unloading = true;

        Service.Framework.Update -= OnFrameworkUpdate;

        Service.Framework.RunOnFrameworkThread(Update);

        foreach (Cmd cmd in _cmds)
        {
            cmd.Dispose();
        }

        Service.PluginUI.Dispose();

        Service.Plugin = null!;
    }

    public void Update()
    {
        var dev = Device.Instance();

        var width = dev->Width;
        var height = dev->Height;

        PInvoke.GetClientRect((HWND) (IntPtr) dev->hWnd, out var rect);

        if (Service.Config.IsScale || _unloading)
        {
            var scale = _unloading ? 1f : Service.Config.Scale;

            width = (uint) Math.Round(rect.Width * scale);
            height = (uint) Math.Round(rect.Height * scale);
        }
        else
        {
            width = Service.Config.Width;
            height = Service.Config.Height;
        }

        if (width != dev->Width || height != dev->Height)
        {
            if (width < 256)
            {
                width = 256;
            }

            if (height < 256)
            {
                height = 256;
            }

            dev->NewWidth = width;
            dev->NewHeight = height;
            dev->RequestResolutionChange = 1;
        }

        CurrentWidth = width;
        CurrentHeight = height;
        CurrentWindowWidth = (uint) rect.Width;
        CurrentWindowHeight = (uint) rect.Height;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (_tickCount++ >= 10)
        {
            _tickCount = 0;

            Update();
        }

        _tickCount++;
    }
}
