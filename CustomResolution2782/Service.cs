using CustomResolution.Hooks;
using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CustomResolution;

public sealed class Service
{

    public static readonly string PluginName = typeof(Plugin).Assembly.GetName().Name!;

    public static Plugin Plugin { get; internal set; } = null!;

    public static DisplaySizeState DisplaySize { get; internal set; } = null!;
    public static GameSizeState GameSize { get; internal set; } = null!;

    public static Configuration Config { get; internal set; } = null!;

    public static DebugConfiguration DebugConfig { get; internal set; } = null!;

    public static PluginUI PluginUI { get; internal set; } = null!;

    public static WndProcHook WndProcHook { get; internal set; } = null!;
    public static CursorPosHooks CursorPosHooks { get; internal set; } = null!;
    public static WindowRectHooks WindowRectHooks { get; internal set; } = null!;

    [PluginService]
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    public static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    public static IFramework Framework { get; private set; } = null!;

    [PluginService]
    public static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

    [PluginService]
    public static ISigScanner SigScanner { get; private set; } = null!;

    [PluginService]
    public static IPluginLog PluginLog { get; private set; } = null!;

    [PluginService]
    public static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    public static IKeyState KeyState { get; private set; } = null!;

    [PluginService]
    public static IGameConfig GameConfig { get; private set; } = null!;

    public static void PrintChat(string msg)
    {
        ChatGui.Print(new XivChatEntry
        {
            Message = msg,
            Type = PluginInterface.GeneralChatType
        });
    }

    public static void PrintChatErr(string msg)
    {
        ChatGui.PrintError(msg);
    }
}
