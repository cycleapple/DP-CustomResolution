using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private readonly HRGN _invisibleRgn;

    private readonly List<Cmd> _cmds;
    private bool _unloading = false;
    private int _tickCount = 0;
    private HWND _currentHwnd;
    private RECT _currentClientRect;
    private RECT _currentWindowRect;
    private bool _currentDXVKDWMHack = false;

    public Plugin([RequiredVersion("1.0")] DalamudPluginInterface pluginInterface)
    {
        _invisibleRgn = CreateRectRgn(0, 0, -1, -1);

        pluginInterface.Create<Service>();

        Service.Plugin = this;

        Service.Config = Service.PluginInterface.GetPluginConfig() as Configuration ?? new();
        Service.Config.Initialize(Service.PluginInterface);

        Service.PluginUI = new();

        Service.WndProcHook = new();
        Service.CursorPosHooks = new();

        _cmds = typeof(Plugin).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Cmd).IsAssignableFrom(t))
            .Select(t => (Cmd) Activator.CreateInstance(t)!)
            .ToList();

        foreach (Cmd cmd in _cmds)
        {
            cmd.Register(Service.CommandManager);
        }

        Service.Framework.Update += OnFrameworkUpdate;
    }

    public string Name => "CustomResolution";

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }
    public uint CurrentWindowWidth { get; private set; }
    public uint CurrentWindowHeight { get; private set; }

    public void Dispose()
    {
        _tickCount = 0;
        _unloading = true;

        Service.Framework.Update -= OnFrameworkUpdate;

        Service.Framework.RunOnFrameworkThread(Update);

        foreach (Cmd cmd in _cmds)
        {
            cmd.Dispose();
        }

        Service.CursorPosHooks.Dispose();
        Service.WndProcHook.Dispose();

        Service.PluginUI.Dispose();

        Service.Plugin = null!;
    }

    public void ConvertCoordsWinToGame(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth &&  CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        float scaleX = CurrentWidth / (float) CurrentWindowWidth;
        float scaleY = CurrentHeight / (float) CurrentWindowHeight;

        x = (int) Math.Round(x * scaleX);
        y = (int) Math.Round(y * scaleY);
    }

    public void ConvertCoordsGameToWin(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        float scaleX = CurrentWindowWidth / (float) CurrentWidth;
        float scaleY = CurrentWindowHeight / (float) CurrentHeight;

        x = (int) Math.Round(x * scaleX);
        y = (int) Math.Round(y * scaleY);
    }

    public void ConvertCoordsGlobalToGame(ref int x, ref int y)
    {
        if (CurrentWidth == CurrentWindowWidth && CurrentHeight == CurrentWindowHeight)
        {
            return;
        }

        float scaleX = CurrentWidth / (float) CurrentWindowWidth;
        float scaleY = CurrentHeight / (float) CurrentWindowHeight;

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

        float scaleX = CurrentWindowWidth / (float) CurrentWidth;
        float scaleY = CurrentWindowHeight / (float) CurrentHeight;

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

        int rectWidth = _currentClientRect.right - _currentClientRect.left;
        int rectHeight = _currentClientRect.bottom - _currentClientRect.top;

        if ((rectWidth <= 0 || rectHeight <= 0) && !_unloading)
        {
            return;
        }

        uint width, height;

        bool disabled = _unloading || !Service.Config.IsEnabled;

        if (Service.Config.IsScale || disabled)
        {
            var scale = disabled ? 1f : Service.Config.Scale;

            width = (uint) Math.Round(rectWidth * scale);
            height = (uint) Math.Round(rectHeight * scale);
        }
        else
        {
            width = Service.Config.Width;
            height = Service.Config.Height;
        }

        if (width != dev->Width || height != dev->Height)
        {
            Service.PluginLog.Info($"Changing resolution to {width} x {height}");

            if (width < 256)
            {
                width = 256;
            }

            if (height < 256)
            {
                height = 256;
            }

            dev->NewWidth = width;
            dev->NewHeight = height;
            dev->RequestResolutionChange = 1;
        }

        if (Service.Config.IsDXVKDWMHackEnabled && !_unloading)
        {
#if false
            Service.PluginLog.Info($"STYLE: 0x{GetWindowLong(_currentHwnd, GWL.GWL_STYLE):X8}");
            Service.PluginLog.Info($"EXSTYLE: 0x{GetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE):X8}");

            Span<ushort> name = stackalloc ushort[256];
            GetClassName(_currentHwnd, name.GetPointer(0), name.Length);
            WNDCLASSEXW wce;
            GetClassInfoEx(GetModuleHandle(null), name.GetPointer(0), &wce);

            Service.PluginLog.Info($"CLASS: {new string((char*) name.GetPointer(0))}");
            Service.PluginLog.Info($"CLASS.style: 0x{wce.style:X8}");
#endif

            SetDXVKDWMHack(true);
        }
        else if (!Service.Config.IsDXVKDWMHackEnabled && _currentDXVKDWMHack)
        {
            SetDXVKDWMHack(false);
        }

        _currentDXVKDWMHack = Service.Config.IsDXVKDWMHackEnabled;

        CurrentWidth = width;
        CurrentHeight = height;
        CurrentWindowWidth = (uint) rectWidth;
        CurrentWindowHeight = (uint) rectHeight;
    }

    private void SetDXVKDWMHack(bool enabled)
    {
        /* Default maximized style / exstyle is 0x95000000 / 0.
         * WS.WS_POPUP | WS.WS_VISIBLE | WS.WS_CLIPSIBLINGS | WS.WS_MAXIMIZE
         * Default windowed style / exstyle is 0x14CF0000 / 0.
         * WS.WS_VISIBLE | WS.WS_CLIPSIBLINGS | WS.WS_CAPTION | WS.WS_SYSMENU | WS.WS_THICKFRAME | WS.WS_MINIMIZEBOX | WS.WS_MAXIMIZEBOX
         */

        uint style = (uint) GetWindowLong(_currentHwnd, GWL.GWL_STYLE);
        bool fullscreen = (style & WS.WS_SYSMENU) == 0;

        /* Alternative hacks:
         * - WS_EX_CLIENTEDGE, at the cost of having a 2px border on each side.
         */

        if (fullscreen)
        {
            if (enabled)
            {
                style &= ~WS.WS_POPUP;
            }
            else
            {
                style |= WS.WS_POPUP;
            }
        }

        SetWindowLong(_currentHwnd, GWL.GWL_STYLE, (int) style);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        var dev = Device.Instance();

        _currentHwnd = (HWND) (IntPtr) dev->hWnd;

        fixed (RECT* currentClientRectPtr = &_currentClientRect)
        fixed (RECT* currentWindowRectPtr = &_currentWindowRect)
        {
            GetClientRect(_currentHwnd, currentClientRectPtr);
            GetWindowRect(_currentHwnd, currentWindowRectPtr);
        }

        if (_tickCount++ >= 10)
        {
            _tickCount = 0;

            Update();
        }

        _tickCount++;
    }
}
