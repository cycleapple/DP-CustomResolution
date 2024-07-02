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
    private DXVKDWMHackMode _currentDXVKDWMHackMode = DXVKDWMHackMode.Off;

    public Plugin(IDalamudPluginInterface pluginInterface)
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

    public HWND CurrentHWND { get; private set; }

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }
    public uint CurrentWindowWidth { get; private set; }
    public uint CurrentWindowHeight { get; private set; }

    public bool CurrentBorderlessFullscreen { get; private set; }

    public bool IsDebug { get; set; }

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

        // TODO: This isn't accurate! Figure out how to read the game's settings instead.
        CurrentBorderlessFullscreen = (GetWindowLong(_currentHwnd, GWL.GWL_STYLE) & WS.WS_SYSMENU) == 0;

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
        uint styleOrig = (uint) GetWindowLong(_currentHwnd, GWL.GWL_STYLE);
        uint exstyleOrig = (uint) GetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE);

        uint style = styleOrig;
        uint exstyle = exstyleOrig;

        bool fullscreen = (style & WS.WS_SYSMENU) == 0;

        if (IsDebug)
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

        if (IsDebug)
        {
            Service.PluginLog.Info($"NEWSTYLE: 0x{style:X8}");
            Service.PluginLog.Info($"NEWEXSTYLE: 0x{exstyle:X8}");
        }

        if (style != styleOrig || exstyle != exstyleOrig || _currentDXVKDWMHackMode != mode)
        {
            if (IsDebug)
            {
                Service.PluginLog.Info("UPDATE");
            }

            SetWindowLong(_currentHwnd, GWL.GWL_STYLE, (int) style);
            SetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE, (int) exstyle);

            SetWindowPos(_currentHwnd, HWND.NULL, 0, 0, 0, 0, SWP.SWP_NOZORDER | SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE | SWP.SWP_DRAWFRAME);
            ShowWindow(_currentHwnd, SW.SW_SHOW);
        }
        else if (IsDebug)
        {
            Service.PluginLog.Info("SAME");
        }

        _currentDXVKDWMHackMode = mode;
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
