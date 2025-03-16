using System.Globalization;
using System;

namespace CustomResolution.Cmds;

public sealed class MainCmd : Cmd
{
    public override string Name => "cres";

    public override string HelpMessage => $"Open / adjust the CustomResolution settings.\n" +
        $"\tExamples:\n" +
        $"\tTo open the settings:\n\t\t{FullName}\n" +
        $"\tTo enable / disable it:\n\t\t{FullName} on\n\t\t{FullName} off\n\t\t{FullName} toggle\n" +
        $"\tTo set the scale:\n\t\t{FullName} 1.5\n" +
        $"\tTo set the resolution:\n\t\t{FullName} 1920 1080";

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

            Service.Config.IsScale = false;
            Service.Config.Width = width;
            Service.Config.Height = height;
            Service.Config.Save();

            Service.PrintChat("Updated custom resolution.");
            return;
        }

        switch (arguments.ToLowerInvariant())
        {
            case "on":
                Service.Config.IsEnabled = true;
                Service.Config.Save();
                Service.PrintChat("Enabled custom resolution.");
                return;

            case "off":
                Service.Config.IsEnabled = false;
                Service.Config.Save();
                Service.PrintChat("Disabled custom resolution.");
                return;

            case "toggle":
                Service.Config.IsEnabled = !Service.Config.IsEnabled;
                Service.Config.Save();
                Service.PrintChat($"{(Service.Config.IsEnabled ? "Enabled" : "Disabled")} custom resolution.");
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
                Service.DebugConfig.SetSizeMode = SetSizeMode.LegacyHookSetWindowPos;
                Service.PrintChat("Switched to legacy size mode. Please report your use case / any bugs with the new mode to 0x0ade.");
                return;

            case "debugsizenew":
                Service.DebugConfig.SetSizeMode = SetSizeMode.InterceptSystemConfig;
                Service.PrintChat("Switched back to new size mode.");
                return;
        }

        if (!float.TryParse(arguments, CultureInfo.InvariantCulture, out float value))
        {
            Service.PrintChat("Invalid parameters.");
            return;
        }
        
        Service.Config.IsScale = true;
        Service.Config.Scale = value;
        Service.Config.Save();

        Service.PrintChat("Updated custom resolution scale.");
    }
}
