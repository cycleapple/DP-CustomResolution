using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Config;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private object _disposeLock = new();

    private readonly HRGN _invisibleRgn;

    private readonly List<Cmd> _cmds;
    private bool _unloading = false;
    private bool _wasEnabled = false;
    private HWND _currentHwnd;
    private bool _currentHwndMismatch = false;
    private RECT _currentClientRect;
    private DXVKDWMHackMode _currentDXVKDWMHackMode = DXVKDWMHackMode.Off;
    private bool _ignoreConfigChanges = false;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        _invisibleRgn = CreateRectRgn(0, 0, -1, -1);

        pluginInterface.Create<Service>();

        Service.Plugin = this;

        Service.DebugConfig = new DebugConfiguration();

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

    public HWND CurrentHWND { get; private set; }

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }
    public uint CurrentWindowWidth { get; private set; }
    public uint CurrentWindowHeight { get; private set; }

    public bool CurrentBorderlessFullscreen { get; private set; }

    public void Dispose()
    {
        _unloading = true;

        lock (_disposeLock)
        {
            Service.Framework.Update -= OnFrameworkUpdate;
            Service.GameConfig.SystemChanged -= OnSystemConfigChanged;

            Service.Framework.RunOnFrameworkThread(Update);
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

    public void ConvertCoordsWinToGame(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        var scaleX = CurrentWidth / (float) CurrentWindowWidth;
        var scaleY = CurrentHeight / (float) CurrentWindowHeight;

        x = (int) Math.Round(x * scaleX);
        y = (int) Math.Round(y * scaleY);
    }

    public void ConvertCoordsGameToWin(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        var scaleX = CurrentWindowWidth / (float) CurrentWidth;
        var scaleY = CurrentWindowHeight / (float) CurrentHeight;

        x = (int) Math.Round(x * scaleX);
        y = (int) Math.Round(y * scaleY);
    }

    public void ConvertCoordsGlobalToGame(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        var scaleX = CurrentWidth / (float) CurrentWindowWidth;
        var scaleY = CurrentHeight / (float) CurrentWindowHeight;

        var p = new POINT(x, y);

        ScreenToClient(_currentHwnd, &p);

        p.x = (int) Math.Round(p.x * scaleX);
        p.y = (int) Math.Round(p.y * scaleY);

        ClientToScreen(_currentHwnd, &p);

        x = p.x;
        y = p.y;
    }

    public void ConvertCoordsGameToGlobal(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        var scaleX = CurrentWindowWidth / (float) CurrentWidth;
        var scaleY = CurrentWindowHeight / (float) CurrentHeight;

        var p = new POINT(x, y);

        ScreenToClient(_currentHwnd, &p);

        p.x = (int) Math.Round(p.x * scaleX);
        p.y = (int) Math.Round(p.y * scaleY);

        ClientToScreen(_currentHwnd, &p);

        x = p.x;
        y = p.y;
    }

    public void Update()
    {
        var dev = Device.Instance();
        var framework = Framework.Instance();
        var win = framework->GameWindow;

        _currentHwnd = (HWND) win->WindowHandle;

#if CRES_CLEAN
        var _dev_hWnd = (IntPtr*) &dev->hWnd;
        var _dev_NewWidth = &dev->NewWidth;
        var _dev_NewHeight = &dev->NewHeight;
        var _dev_RequestResolutionChange = &dev->RequestResolutionChange;
#else
        // 7.2 adds 1B8 before hWnd (previously 820, now 9D8)
        var _dev_hWnd = (nint*) ((IntPtr) dev + 0x9D8);
        var _dev_NewWidth = (uint*) ((IntPtr) dev + 0x9D8 + 0x10);
        var _dev_NewHeight = (uint*) ((IntPtr) dev + 0x9D8 + 0x14);
        var _dev_RequestResolutionChange = &dev->RequestResolutionChange;
#endif

        // As a safety measure, don't mess with the structs if we're reading garbage.
        // This isn't perfect, but it's better than nothing.
        if (_currentHwnd != *_dev_hWnd)
        {
            if (!_currentHwndMismatch)
            {
                _currentHwndMismatch = true;
                Service.PluginLog.Error($"HWND MISMATCH between GameWindow and Device: 0x{(long) (IntPtr) (_currentHwnd):X16} vs 0x{(long) *_dev_hWnd:X16}");
                Service.PluginLog.Info($"dev is at: 0x{(long) (IntPtr) dev:X16}");
                CheckHWND("GameWindow", _currentHwnd);
                CheckHWND("Device", (HWND) (*_dev_hWnd));
            }

            return;
        }

        _currentHwndMismatch = false;

        fixed (RECT* currentClientRectPtr = &_currentClientRect)
        {
            Service.WindowRectHooks.GetClientRectOrig(_currentHwnd, currentClientRectPtr);
        }

        var rectWidth = _currentClientRect.right - _currentClientRect.left;
        var rectHeight = _currentClientRect.bottom - _currentClientRect.top;

        if ((rectWidth <= 0 || rectHeight <= 0) && !_unloading)
        {
            return;
        }

        uint width, height;

        var enabled = !_unloading && Service.Config.IsEnabled;
        if (_wasEnabled != enabled)
        {
            Service.PluginLog.Info($"Changing state to: {enabled}");
            _wasEnabled = enabled;
        }

        if (Service.Config.IsScale || !enabled)
        {
            var scale = enabled ? Service.Config.Scale : 1f;

            width = (uint) Math.Round(rectWidth * scale);
            height = (uint) Math.Round(rectHeight * scale);
        }
        else
        {
            width = Service.Config.Width;
            height = Service.Config.Height;
        }

        if (width < 256)
        {
            width = 256;
        }

        if (height < 256)
        {
            height = 256;
        }

        if (width != dev->Width || height != dev->Height)
        {
            Service.PluginLog.Info($"Changing graphics resolution from {dev->Width} x {dev->Height} to {width} x {height}");
            *_dev_NewWidth = width;
            *_dev_NewHeight = height;
            *_dev_RequestResolutionChange = 1;
            Service.PluginLog.Info($"Changing game window from {win->WindowWidth} x {win->WindowHeight} to {width} x {height}");
            win->WindowWidth = (int) width;
            win->WindowHeight = (int) height;
        }

        //Service.PluginLog.Debug($"NewWidth 0x{(long) (IntPtr) (&dev->NewWidth):X16}");
        //Service.PluginLog.Debug($"GameWindow->Width 0x{(long) (IntPtr) (&win->WindowWidth):X16}");

        //Service.PluginLog.Info($"Game window at {win->WindowWidth} x {win->WindowHeight}");

        //Service.PluginLog.Info($"AAA {Service.GameConfig.System.GetUInt(nameof(SystemConfigOption.UiHighScale))}");

        CurrentBorderlessFullscreen = win->Borderless;

        if (Service.Config.DXVKDWMHackMode != DXVKDWMHackMode.Off && !_unloading)
        {
            SetDXVKDWMHack(Service.Config.DXVKDWMHackMode);
        }
        else if (Service.Config.DXVKDWMHackMode == DXVKDWMHackMode.Off && _currentDXVKDWMHackMode != DXVKDWMHackMode.Off)
        {
            SetDXVKDWMHack(DXVKDWMHackMode.Off);
        }

        CurrentWidth = width;
        CurrentHeight = height;
        CurrentWindowWidth = (uint) rectWidth;
        CurrentWindowHeight = (uint) rectHeight;
    }

    private void SetDXVKDWMHack(DXVKDWMHackMode mode)
    {
        /* Default maximized style / exstyle is 0x95000000 / 0.
         * WS.WS_POPUP | WS.WS_VISIBLE | WS.WS_CLIPSIBLINGS | WS.WS_MAXIMIZE
         * Default windowed style / exstyle is 0x14CF0000 / 0.
         * WS.WS_VISIBLE | WS.WS_CLIPSIBLINGS | WS.WS_CAPTION | WS.WS_SYSMENU | WS.WS_THICKFRAME | WS.WS_MINIMIZEBOX | WS.WS_MAXIMIZEBOX
         */
        var styleOrig = (uint) GetWindowLong(_currentHwnd, GWL.GWL_STYLE);
        var exstyleOrig = (uint) GetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE);

        var style = styleOrig;
        var exstyle = exstyleOrig;

        var fullscreen = (style & WS.WS_SYSMENU) == 0;

        if (Service.DebugConfig.IsDebug)
        {
            Service.PluginLog.Info("--------");
            Service.PluginLog.Info($"STYLE: 0x{style:X8}");
            Service.PluginLog.Info($"EXSTYLE: 0x{GetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE):X8}");

            Span<ushort> name = stackalloc ushort[256];
            GetClassName(_currentHwnd, name.GetPointer(0), name.Length);
            WNDCLASSEXW wce;
            GetClassInfoEx(GetModuleHandle(null), name.GetPointer(0), &wce);

            Service.PluginLog.Info($"CLASS: {new string((char*) name.GetPointer(0))}");
            Service.PluginLog.Info($"CLASS.style: 0x{wce.style:X8}");
        }

        if (fullscreen)
        {
            if (mode == DXVKDWMHackMode.UnsetPopup)
            {
                style &= ~WS.WS_POPUP;
            }
            else
            {
                style |= WS.WS_POPUP;
            }
        }

        if (fullscreen && mode.IsSetClientEdge())
        {
            exstyle |= WS.WS_EX_CLIENTEDGE;
            exstyle |= WS.WS_EX_COMPOSITED;
        }
        else
        {
            exstyle &= ~(uint) WS.WS_EX_CLIENTEDGE;
            exstyle &= ~(uint) WS.WS_EX_COMPOSITED;
        }

        if (Service.DebugConfig.IsDebug)
        {
            Service.PluginLog.Info($"NEWSTYLE: 0x{style:X8}");
            Service.PluginLog.Info($"NEWEXSTYLE: 0x{exstyle:X8}");
        }

        if (style != styleOrig || exstyle != exstyleOrig || _currentDXVKDWMHackMode != mode)
        {
            if (Service.DebugConfig.IsDebug)
            {
                Service.PluginLog.Info("UPDATE");
            }

            SetWindowLong(_currentHwnd, GWL.GWL_STYLE, (int) style);
            SetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE, (int) exstyle);

            SetWindowPos(_currentHwnd, HWND.NULL, 0, 0, 0, 0, SWP.SWP_NOZORDER | SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE | SWP.SWP_DRAWFRAME);
            ShowWindow(_currentHwnd, SW.SW_SHOW);
        }
        else if (Service.DebugConfig.IsDebug)
        {
            Service.PluginLog.Info("SAME");
        }

        _currentDXVKDWMHackMode = mode;
    }

    private void CheckHWND(string from, HWND hwnd)
    {
        RECT rect = default;
        if (!GetClientRect(hwnd, &rect))
        {
            Service.PluginLog.Info($"{from} is sus: {Marshal.GetLastPInvokeErrorMessage()}");
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        lock (_disposeLock)
        {
            var io = ImGui.GetIO();

            if (Service.Config.HotkeyKey != VirtualKey.NO_KEY &&
                (ImGuiNative.igIsKeyPressed((ImGuiKey) Service.Config.HotkeyKey, 0) != 0 || Service.KeyState.GetRawValue(Service.Config.HotkeyKey) != 0) &&
                (Service.Config.HotkeyModifier switch
                {
                    ModifierKey.NONE => !io.KeyCtrl && !io.KeyAlt && !io.KeyShift,
                    ModifierKey.CTRL => io.KeyCtrl && !io.KeyAlt && !io.KeyShift,
                    ModifierKey.ALT => !io.KeyCtrl && io.KeyAlt && !io.KeyShift,
                    ModifierKey.SHIFT => !io.KeyCtrl && !io.KeyAlt && io.KeyShift,
                    _ => false,
                }))
            {
                Service.Config.IsEnabled = !Service.Config.IsEnabled;
                Service.Config.Save();
            }

            Update();
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
                if (Service.DebugConfig.SetSizeMode == SetSizeMode.InterceptSystemConfig)
                {
                    var name = e.ConfigOption.ToString();
                    var valueOrig = Service.GameConfig.System.GetUInt(name);

                    var isW = e.ConfigOption == SystemConfigOption.ScreenWidth;
                    var scale = (isW ? CurrentWindowWidth : CurrentWindowHeight) / (float) (isW ? CurrentWidth : CurrentHeight);
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
