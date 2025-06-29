using System.Globalization;
using System;

namespace CustomResolution.Cmds;

public sealed class MainCmd : Cmd
{
    public override string Name => "cres";

    public override string HelpMessage => $"Open / adjust the CustomResolution display scale settings.\n" +
        $"\tExamples:\n" +
        $"\tTo open the settings:\n\t\t{FullName}\n" +
        $"\tTo enable / disable it:\n\t\t{FullName} on\n\t\t{FullName} off\n\t\t{FullName} toggle\n" +
        $"\tTo set the display scale:\n\t\t{FullName} 1.5\n" +
        $"\tTo set the display resolution:\n\t\t{FullName} 1920 1080";

    public override void Run(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            Service.PluginUI.SettingsVisible = !Service.PluginUI.SettingsVisible;

            return;
        }

        int indexOfSplit = arguments.IndexOf(' ');

        if (indexOfSplit != -1)
        {
            if (!uint.TryParse(arguments.AsSpan(0, indexOfSplit), CultureInfo.InvariantCulture, out var width) ||
                !uint.TryParse(arguments.AsSpan(indexOfSplit + 1), CultureInfo.InvariantCulture, out var height))
            {
                Service.PrintChat("Invalid parameters.");
                return;
            }

            Service.Config._.Display.IsScale = false;
            Service.Config._.Display.Width = width;
            Service.Config._.Display.Height = height;
            Service.Config.Save();

            Service.PrintChat("Updated custom display resolution.");
            return;
        }

        switch (arguments.ToLowerInvariant())
        {
            case "on":
                Service.Config._.Display.IsEnabled = true;
                Service.Config.Save();
                Service.PrintChat("Enabled custom display resolution.");
                return;

            case "off":
                Service.Config._.Display.IsEnabled = false;
                Service.Config.Save();
                Service.PrintChat("Disabled custom display resolution.");
                return;

            case "toggle":
                Service.Config._.Display.IsEnabled = !Service.Config._.Display.IsEnabled;
                Service.Config.Save();
                Service.PrintChat($"{(Service.Config._.Display.IsEnabled ? "Enabled" : "Disabled")} custom display resolution.");
                return;

            case "debugon":
                Service.DebugConfig.IsDebug = true;
                Service.PrintChat("Enabled cres debug.");
                return;

            case "debugoff":
                Service.DebugConfig.IsDebug = false;
                Service.PrintChat("Disabled cres debug.");
                return;

            case "debug":
                Service.DebugConfig.IsDebug = !Service.DebugConfig.IsDebug;
                Service.PrintChat($"{(Service.DebugConfig.IsDebug ? "Enabled" : "Disabled")} cres debug.");
                return;

            case "debugsizeold":
                Service.DebugConfig.ForceSizeMode = ForceSizeMode.LegacyRequestResolutionChange;
                Service.PrintChat("Switched to legacy size mode. Please report your use case / any bugs with the new mode to 0x0ade.");
                return;

            case "debugsizenew":
                Service.DebugConfig.ForceSizeMode = ForceSizeMode.FakeWindowResize;
                Service.PrintChat("Switched back to new size mode.");
                return;
        }

        if (!float.TryParse(arguments, CultureInfo.InvariantCulture, out float value))
        {
            Service.PrintChat("Invalid parameters.");
            return;
        }
        
        Service.Config._.Display.IsScale = true;
        Service.Config._.Display.Scale = value;
        Service.Config.Save();

        Service.PrintChat("Updated custom display resolution scale.");
    }
}
