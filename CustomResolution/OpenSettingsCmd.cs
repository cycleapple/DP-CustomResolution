namespace CustomResolution;

public sealed class OpenSettingsCmd : Cmd
{
    public override string Name => "cres";

    public override string HelpMessage => "Open the CustomResolution Settings";

    public override void Run(string arguments)
    {
        Service.PluginUI.SettingsVisible = !Service.PluginUI.SettingsVisible;
    }
}
