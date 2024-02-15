using CustomResolution.Hooks;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CustomResolution;

public sealed class Service
{
    public static Plugin Plugin { get; internal set; } = null!;

    public static Configuration Config { get; internal set; } = null!;

    public static PluginUI PluginUI { get; internal set; } = null!;

    public static WndProcHook WndProcHook { get; internal set; } = null!;
    public static CursorPosHooks CursorPosHooks { get; internal set; } = null!;

    [PluginService]
    public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    [PluginService]
    public static IPluginLog PluginLog { get; private set; } = null!;
}
