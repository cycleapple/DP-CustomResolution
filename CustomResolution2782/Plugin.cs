using CustomResolution.Hooks;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Config;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private object _disposeLock = new();

    private readonly List<Cmd> _cmds;
    private bool _ignoreConfigChanges = false;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        Service.Plugin = this;

        Service.DebugConfig = new();
        Service.DisplaySize = new();
        Service.GameSize = new();

        Service.Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new();
        Service.Config.Initialize(Service.PluginInterface);

        Service.PluginUI = new();

        Service.WndProcHook = new();
        Service.CursorPosHooks = new();
        Service.WindowRectHooks = new();

        _cmds = typeof(Plugin).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Cmd).IsAssignableFrom(t))
            .Select(t => (Cmd) Activator.CreateInstance(t)!)
            .ToList();

        foreach (Cmd cmd in _cmds)
        {
            cmd.Register(Service.CommandManager);
        }

        Service.Framework.Update += OnFrameworkUpdate;
        Service.GameConfig.SystemChanged += OnSystemConfigChanged;
    }

    public string Name => "CustomResolution";

    public bool Unloading { get; private set; } = false;

    public void Dispose()
    {
        Unloading = true;

        lock (_disposeLock)
        {
            Service.Framework.Update -= OnFrameworkUpdate;
            Service.GameConfig.SystemChanged -= OnSystemConfigChanged;
            Service.DisplaySize.Dispose();
            Service.GameSize.Dispose();
        }

        foreach (Cmd cmd in _cmds)
        {
            cmd.Dispose();
        }

        Service.WindowRectHooks.Dispose();
        Service.CursorPosHooks.Dispose();
        Service.WndProcHook.Dispose();

        Service.PluginUI.Dispose();

        Service.Plugin = null!;
    }

    private void OnFrameworkUpdateForSize(ref ConfigurationV1.SizeConfig size)
    {
        if (size.HotkeyKey == VirtualKey.NO_KEY)
        {
            return;
        }

        var io = ImGui.GetIO();
        var keyPressedIm = ImGuiNative.igIsKeyPressed((ImGuiKey) size.HotkeyKey, 0) != 0;
        var keyDownXiv = Service.KeyState.GetRawValue(size.HotkeyKey) != 0;

        if ((keyPressedIm || (keyDownXiv && !size.HotkeyKeyDown)) &&
            (size.HotkeyModifier switch
            {
                ModifierKey.NONE => !io.KeyCtrl && !io.KeyAlt && !io.KeyShift,
                ModifierKey.CTRL => io.KeyCtrl && !io.KeyAlt && !io.KeyShift,
                ModifierKey.ALT => !io.KeyCtrl && io.KeyAlt && !io.KeyShift,
                ModifierKey.SHIFT => !io.KeyCtrl && !io.KeyAlt && io.KeyShift,
                _ => false,
            }))
        {
            size.IsEnabled = !size.IsEnabled;
            Service.Config.Save();
        }

        size.HotkeyKeyDown = keyDownXiv;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        lock (_disposeLock)
        {
            OnFrameworkUpdateForSize(ref Service.Config._.Display);
            OnFrameworkUpdateForSize(ref Service.Config._.Game);

            Service.DisplaySize.Update();
            Service.GameSize.Update();
        }
    }

    private void OnSystemConfigChanged(object? sender, ConfigChangeEvent raw)
    {
        if (_ignoreConfigChanges)
        {
            return;
        }

        if (raw is not ConfigChangeEvent<SystemConfigOption> { } e)
        {
            return;
        }

        switch (e.ConfigOption)
        {
            case SystemConfigOption.ScreenWidth:
            case SystemConfigOption.ScreenHeight:
                if (Service.DebugConfig.SetWindowSizeMode == SetWindowSizeMode.InterceptSystemConfig)
                {
                    var d = Service.DisplaySize;
                    var name = e.ConfigOption.ToString();
                    var valueOrig = Service.GameConfig.System.GetUInt(name);

                    var isW = e.ConfigOption == SystemConfigOption.ScreenWidth;
                    var scale = (isW ? d.CurrentWindowWidth : d.CurrentWindowHeight) / (float) (isW ? d.CurrentWidth : d.CurrentHeight);
                    var valueNew = (uint) Math.Round(valueOrig * scale);

                    Service.PluginLog.Info($"Intercepting config value change for {name}: {valueOrig} -> {valueNew}");
                    try
                    {
                        _ignoreConfigChanges = true;
                        Service.GameConfig.System.Set(name, valueNew);
                    }
                    finally
                    {
                        _ignoreConfigChanges = false;
                    }
                }

                break;
        }
    }
}
