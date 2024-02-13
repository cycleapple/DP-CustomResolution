using Dalamud.IoC;
using Dalamud.Plugin;
using System;
using Dalamud.Plugin.Services;

namespace CustomResolution;

public sealed class Service
{
    public static Plugin Plugin { get; internal set; } = null!;

    public static Configuration Config { get; internal set; } = null!;

    public static PluginUI PluginUI { get; internal set; } = null!;

    [PluginService]
    public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IPluginLog PluginLog { get; private set; } = null!;

    public static Func<string, object?>? GetPluginInstance { get; private set; }
}
