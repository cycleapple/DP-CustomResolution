using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using System;
using System.Linq;
using System.Numerics;

namespace CustomResolution.Windows;

public class ConfigWindow : Window, IDisposable
{
    private VirtualKey[] _validKeys = new VirtualKey[] { VirtualKey.NO_KEY }.Union(Service.KeyState.GetValidVirtualKeys()).ToArray();

    private readonly int[] _displayCurrentWH = new int[2];
    private readonly int[] _displayCurrentWindowWH = new int[2];
    private readonly int[] _displayCurrentGameWH = new int[2];

    private ConfigurationV1 _;

    public ConfigWindow() : base(
        "CustomResolution",
        ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysAutoResize)
    {
        UpdateFromConfig();
    }

    public void Dispose() {
    }

    public void UpdateFromConfig()
    {
        _ = Service.Config._;
    }

    public void UpdateToConfig()
    {
        Service.Config._ = _;
        Service.Config.Save();
    }

    public override void Draw()
    {
        var save = false;

        _displayCurrentWH[0] = (int) Service.DisplaySize.CurrentWidth;
        _displayCurrentWH[1] = (int) Service.DisplaySize.CurrentHeight;
        _displayCurrentWindowWH[0] = (int) Service.DisplaySize.CurrentWindowWidth;
        _displayCurrentWindowWH[1] = (int) Service.DisplaySize.CurrentWindowHeight;
        _displayCurrentGameWH[0] = (int) Service.GameSize.CurrentWidth;
        _displayCurrentGameWH[1] = (int) Service.GameSize.CurrentHeight;

        using (ImRaii.Disabled())
        {
            ImGui.InputInt2("Window size", ref _displayCurrentWindowWH[0]);
        }

        using (var imTabBar = ImRaii.TabBar("MalfunctionMainTabs"))
        {
            DrawDisplayTab();
            DrawGameTab();
            DrawCommonTab();
        }

        if (ImGui.Button("Save and apply"))
        {
            save = true;
        }

        if (save)
        {
            UpdateToConfig();
        }
    }

    private void DrawDisplayTab()
    {
        using var imTab = ImRaii.TabItem($"Display##DisplayTab");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"The display resolution affects everything,
including screenshots taken in-game and via ReShade.

This works best with the ""High Resolution UI Settings""
in the system configuration, as it also affects the UI.

In general: Use it as a fake fullscreen / window resolution,
best suited for screenshots.
Changed via /cres");
        }

        if (!imTab.Success)
        {
            return;
        }

        using (ImRaii.Disabled())
        {
            ImGui.InputInt2("Current size", ref _displayCurrentWH[0]);
        }

        DrawSizeTabShared(ref _.Display, "Display");

        ImGui.Checkbox("Use scale", ref _.Display.IsScale);

        if (_.Display.IsScale)
        {
            ImGui.InputFloat("Scale", ref _.Display.Scale, 0.01f, 0.1f, "%.3f", ImGuiInputTextFlags.None);
        }
        else
        {
            unsafe
            {
                ImGui.InputInt2("Size in pixels", ref _.Display.WH[0]);
            }
        }

        if (Service.DebugConfig.IsDebug)
        {
            ImGui.Text(Service.DisplaySize.DebugInfo);
        }
    }

    private void DrawGameTab()
    {
        using var imTab = ImRaii.TabItem($"Gameplay##GameTab");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"The gameplay resolution affects only the quality of your character
and everything around them, leaving the HUD untouched.
This also doesn't touch the screenshot resolution.

This works as an advanced version of the in-game ""3D Resolution Scaling"" graphics options,
allowing you to go smaller than 0.5x for more FPS, or higher than 1x for higher quality.

This overrides dynamic resolution!

In general: Use it for gameplay, not for screenshots.
Changed via /gres");
        }

        if (!imTab.Success)
        {
            return;
        }

        using (ImRaii.Disabled())
        {
            ImGui.InputInt2("Current size", ref _displayCurrentGameWH[0]);
        }

        DrawSizeTabShared(ref _.Game, "Game", scaleOnly: true);

        using (_.Game.Scale > 2f ? ImRaii.PushColor(ImGuiCol.Border, 0xff00ffff) : null)
        {
            ImGui.InputFloat("Scale", ref _.Game.Scale, 0.01f, 0.1f, "%.3f", ImGuiInputTextFlags.None);
        }

        if (_.Game.Scale > 2f && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"Scaling larger than 2x currently doesn't improve antialiasing!
This is still being worked on before the v1.0 release.");
        }

        if (ImGui.BeginCombo("Graphics Upscaling", _.ResolutionScalingMode.ToHumanNameString()))
        {
            foreach (var mode in Enum.GetValues<ResolutionScalingMode>())
            {
                if (ImGui.Selectable(mode.ToHumanNameString(), _.ResolutionScalingMode == mode, mode.IsUnsupported() ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                {
                    _.ResolutionScalingMode = mode;
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && mode.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        if (Service.DebugConfig.IsDebug)
        {
            ImGui.Text(Service.GameSize.DebugInfo);
        }
    }


    private void DrawSizeTabShared(ref ConfigurationV1.SizeConfig size, string name, bool scaleOnly = false)
    {
        ImGui.Checkbox("Enabled", ref size.IsEnabled);

        ImGui.SameLine();
        ImGui.Dummy(new Vector2(20, 0));

        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.BeginCombo($"##_{name}_configHotkeyModifier", size.HotkeyModifier.ToHumanNameString()))
        {
            foreach (var key in Enum.GetValues<ModifierKey>())
            {
                if (ImGui.Selectable(key.ToHumanNameString(), size.HotkeyModifier == key, ImGuiSelectableFlags.None))
                {
                    size.HotkeyModifier = key;
                }

                if (ImGui.IsItemHovered() && key.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        if (ImGui.BeginCombo($"##_{name}_configHotkeyKey", size.HotkeyKey.ToHumanNameString()))
        {
            foreach (var key in _validKeys)
            {
                if (ImGui.Selectable(key.ToHumanNameString(), size.HotkeyKey == key, ImGuiSelectableFlags.None))
                {
                    size.HotkeyKey = key;
                }

                if (ImGui.IsItemHovered() && key.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Text("Hotkey");
    }

    private void DrawCommonTab()
    {
        using var imTab = ImRaii.TabItem($"Common##CommonTab");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"Fixes DXVK borderless window causing black screens when alt-tabbing.
This can *possibly* impact performance, depending on your Windows version and GPU.

Feel free to experiment with this toggle.
Make sure to switch the game to windowed and then back to borderless windowed when changing.

Works even with the scaling above disabled.

Not intended to be used with proper fullscreen.");
        }

        if (!imTab.Success)
        {
            return;
        }

        if (ImGui.BeginCombo("Borderless window workaround", _.DXVKDWMHackMode.ToHumanNameString()))
        {
            foreach (var mode in Enum.GetValues<DXVKDWMHackMode>())
            {
                if (ImGui.Selectable(mode.ToHumanNameString(), _.DXVKDWMHackMode == mode, ImGuiSelectableFlags.None))
                {
                    _.DXVKDWMHackMode = mode;
                }

                if (ImGui.IsItemHovered() && mode.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(@"Smaller tweaks and fixes, f.e. for DXVK on Windows,
or to allow making the window smaller than 1024x768.");
        }

        if (ImGui.BeginCombo("Minimum window size", _.MinSizeMode.ToHumanNameString()))
        {
            foreach (var mode in Enum.GetValues<MinSizeMode>())
            {
                if (ImGui.Selectable(mode.ToHumanNameString(), _.MinSizeMode == mode, ImGuiSelectableFlags.None))
                {
                    _.MinSizeMode = mode;
                }

                if (ImGui.IsItemHovered() && mode.ToHumanInfoString() is { } info)
                {
                    ImGui.SetTooltip(info);
                }
            }

            ImGui.EndCombo();
        }
    }
}
