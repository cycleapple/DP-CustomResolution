using System.Globalization;
using System;

namespace CustomResolution.Cmds;

public sealed class GameResCmd : Cmd
{
    public override string Name => "gres";

    public override string HelpMessage => $"Open / adjust the CustomResolution game scale settings.\n" +
        $"\tExamples:\n" +
        $"\tTo open the settings:\n\t\t{FullName}\n" +
        $"\tTo enable / disable it:\n\t\t{FullName} on\n\t\t{FullName} off\n\t\t{FullName} toggle\n" +
        $"\tTo set the game scale:\n\t\t{FullName} 1.5\n" +
        $"\tTo set the game resolution:\n\t\t{FullName} 1920 1080";

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

            Service.Config._.Game.IsScale = false;
            Service.Config._.Game.Width = width;
            Service.Config._.Game.Height = height;
            Service.Config.Save();

            Service.PrintChat("Updated custom game resolution.");
            return;
        }

        switch (arguments.ToLowerInvariant())
        {
            case "on":
                Service.Config._.Game.IsEnabled = true;
                Service.Config.Save();
                Service.PrintChat("Enabled custom game resolution.");
                return;

            case "off":
                Service.Config._.Game.IsEnabled = false;
                Service.Config.Save();
                Service.PrintChat("Disabled custom game resolution.");
                return;

            case "toggle":
                Service.Config._.Game.IsEnabled = !Service.Config._.Game.IsEnabled;
                Service.Config.Save();
                Service.PrintChat($"{(Service.Config._.Game.IsEnabled ? "Enabled" : "Disabled")} custom game resolution.");
                return;
        }

        if (!float.TryParse(arguments, CultureInfo.InvariantCulture, out float value))
        {
            Service.PrintChat("Invalid parameters.");
            return;
        }
        
        Service.Config._.Game.IsScale = true;
        Service.Config._.Game.Scale = value;
        Service.Config.Save();

        Service.PrintChat("Updated custom game resolution scale.");
    }
}
