using static FFXIVClientStructs.FFXIV.Client.UI.AddonRelicNoteBook;
using System.Collections;
using System.Globalization;
using System;

namespace CustomResolution.Cmds;

public sealed class BorderlessWindowedCmd : Cmd
{
    public override string Name => "cresbw";

    public override string HelpMessage => $"Tweak the \"Apply borderless window workaround\" toggle.\n" +
        $"\tExamples:\n" +
        $"\tTo enable / disable it:\n\t\t{FullName} on\n\t\t{FullName} off\n\t\t{FullName} toggle";

    public override void Run(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            Service.PrintChat("Invalid parameters.");
            return;
        }

        switch (arguments.ToLowerInvariant())
        {
            case "on":
                Service.Config.IsDXVKDWMHackEnabled = true;
                Service.Config.Save();
                Service.PrintChat("Enabled borderless window workaround.");
                return;

            case "off":
                Service.Config.IsDXVKDWMHackEnabled = false;
                Service.Config.Save();
                Service.PrintChat("Disabled borderless window workaround.");
                return;

            case "toggle":
                Service.Config.IsDXVKDWMHackEnabled = !Service.Config.IsDXVKDWMHackEnabled;
                Service.Config.Save();
                Service.PrintChat($"{(Service.Config.IsDXVKDWMHackEnabled ? "Enabled" : "Disabled")} borderless window workaround.");
                return;
        }

        Service.PrintChat("Invalid parameters.");
    }
}
