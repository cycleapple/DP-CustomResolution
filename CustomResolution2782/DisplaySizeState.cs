using CustomResolution.Hooks;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.Interop;
using FloppyUtils;
using FloppyUtils.StructEx.Graphics;
using System;
using System.Runtime.InteropServices;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace CustomResolution;

public unsafe class DisplaySizeState : IDisposable
{
    private StringBuilder _dbg = new();
    private bool _dbgLog = false;
    private bool _wasEnabled = false;
    private HWND _currentHwnd;
    private bool _currentHwndMismatch = false;
    private RECT _currentClientRect;
    private DXVKDWMHackMode _currentDXVKDWMHackMode = DXVKDWMHackMode.Off;
    private bool _currentlyFakeResize = false;
    private bool _requestedResolutionChange = false;

    public HWND CurrentHWND => _currentHwnd;

    public uint CurrentWidth { get; private set; }
    public uint CurrentHeight { get; private set; }
    public uint CurrentWindowWidth { get; private set; }
    public uint CurrentWindowHeight { get; private set; }

    public bool CurrentBorderlessFullscreen { get; private set; }

    public string DebugInfo => _dbg.ToString();

    public void Dispose()
    {
        Service.Framework.RunOnFrameworkThread(Update);
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

        Service.CursorPosHooks.ScreenToClientOrig(_currentHwnd, &p);

        p.x = (int) Math.Round(p.x * scaleX);
        p.y = (int) Math.Round(p.y * scaleY);

        Service.CursorPosHooks.ClientToScreenOrig(_currentHwnd, &p);

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

        Service.CursorPosHooks.ScreenToClientOrig(_currentHwnd, &p);

        p.x = (int) Math.Round(p.x * scaleX);
        p.y = (int) Math.Round(p.y * scaleY);

        Service.CursorPosHooks.ClientToScreenOrig(_currentHwnd, &p);

        x = p.x;
        y = p.y;
    }

    public void Update()
    {
        _dbg.Clear();
        _dbgLog = false;

        bool unloading = Service.Plugin.Unloading;

        ref var cfg = ref Service.Config._.Display;

        var dev = (DeviceEx*) Device.Instance();
        var framework = Framework.Instance();
        var win = framework->GameWindow;

        _currentHwnd = (HWND) win->WindowHandle;

        // As a safety measure, don't mess with the structs if we're reading garbage.
        // This isn't perfect, but it's better than nothing.
        if (_currentHwnd != dev->hWnd)
        {
            if (!_currentHwndMismatch)
            {
                _currentHwndMismatch = true;
                Service.PluginLog.Error($"HWND MISMATCH between GameWindow and Device: 0x{(long) (nint) _currentHwnd:X16} vs 0x{(long) (nint) dev->hWnd:X16}");
                Service.PluginLog.Info($"dev is at: 0x{(long) (nint) dev:X16}");
                CheckHWND("GameWindow", _currentHwnd);
                CheckHWND("Device", (HWND) dev->hWnd);
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

        if ((rectWidth <= 0 || rectHeight <= 0) && !unloading)
        {
            return;
        }

        var enabled = !unloading && cfg.IsEnabled;

        uint width, height;

        if (cfg.IsScale || !enabled)
        {
            var scale = enabled ? cfg.Scale : 1f;

            width = (uint) Math.Round(rectWidth * scale);
            height = (uint) Math.Round(rectHeight * scale);
        }
        else
        {
            width = cfg.Width;
            height = cfg.Height;
        }

        if (width < 256)
        {
            width = 256;
        }

        if (height < 256)
        {
            height = 256;
        }

        _requestedResolutionChange |= dev->RequestResolutionChange != 0;

        if (!_currentlyFakeResize && (unloading || enabled || _wasEnabled || _requestedResolutionChange) &&
            (width != dev->Width || height != dev->Height))
        {
            Service.PluginLog.Info($"Changing graphics resolution from {dev->Width} x {dev->Height} to {width} x {height}");
            var mode = Service.DebugConfig.ForceSizeMode;
            if (_requestedResolutionChange)
            {
                // Let's try to be cautious about other plugins (f.e. SimpleTweaks) changing the game resolution.
                Service.PluginLog.Info($"Game resolution was changed externally - forcing resize via device, not window resize.");
                mode = ForceSizeMode.LegacyRequestResolutionChange;
                _requestedResolutionChange = false;
            }
            switch (mode)
            {
                case ForceSizeMode.Skip:
                    break;
                case ForceSizeMode.LegacyRequestResolutionChange:
                    dev->NewWidth = width;
                    dev->NewHeight = height;
                    dev->RequestResolutionChange = 1;
                    Service.PluginLog.Info($"Changing game window from {win->WindowWidth} x {win->WindowHeight} to {width} x {height}");
                    win->WindowWidth = (int) width;
                    win->WindowHeight = (int) height;
                    break;
                case ForceSizeMode.FakeWindowResize:
                    var adjustedClientRect = new RECT(0, 0, rectWidth, rectHeight);
                    AdjustWindowRect(&adjustedClientRect, (uint) GetWindowLongPtr(_currentHwnd, GWL.GWL_STYLE), false);
                    var adjustedWidth = adjustedClientRect.right - adjustedClientRect.left;
                    var adjustedHeight = adjustedClientRect.bottom - adjustedClientRect.top;
                    Service.PluginLog.Info($"Resizing window to {adjustedWidth} x {adjustedHeight}");
                    try
                    {
                        _currentlyFakeResize = true;
                        SendMessage(_currentHwnd, WM.WM_ENTERSIZEMOVE, 0, 0);
                        SendMessage(_currentHwnd, WM.WM_SIZE, SIZE_MAXSHOW, WndProcHook.SizeToParam((uint) adjustedWidth, (uint) adjustedHeight));
                        SendMessage(_currentHwnd, WM.WM_PAINT, 0, 0);
                        SendMessage(_currentHwnd, WM.WM_EXITSIZEMOVE, 0, 0);
                    }
                    finally
                    {
                        _currentlyFakeResize = false;
                    }
                    break;
                default:
                    Service.PluginLog.Error($"Unknown ForceSizeMode: {Service.DebugConfig.ForceSizeMode}");
                    break;
            }
        }

        if (_wasEnabled != enabled)
        {
            Service.PluginLog.Info($"Changed state to: {enabled}");
            _wasEnabled = enabled;
        }

        //Service.PluginLog.Debug($"NewWidth 0x{(long) (nint) (&dev->NewWidth):X16}");
        //Service.PluginLog.Debug($"GameWindow->Width 0x{(long) (nint) (&win->WindowWidth):X16}");

        //Service.PluginLog.Info($"Game window at {win->WindowWidth} x {win->WindowHeight}");

        //Service.PluginLog.Info($"AAA {Service.GameConfig.System.GetUInt(nameof(SystemConfigOption.UiHighScale))}");

        CurrentBorderlessFullscreen = win->Borderless;

        if (Service.Config._.DXVKDWMHackMode != DXVKDWMHackMode.Off && !unloading)
        {
            SetDXVKDWMHack(Service.Config._.DXVKDWMHackMode);
        }
        else if (Service.Config._.DXVKDWMHackMode == DXVKDWMHackMode.Off && _currentDXVKDWMHackMode != DXVKDWMHackMode.Off)
        {
            SetDXVKDWMHack(DXVKDWMHackMode.Off);
        }

        CurrentWidth = width;
        CurrentHeight = height;
        CurrentWindowWidth = (uint) rectWidth;
        CurrentWindowHeight = (uint) rectHeight;

        if (Service.DebugConfig.IsDebug)
        {
            _dbg.AppendLine(@$"RESIZE {dev->RequestResolutionChange} {dev->RequestResolutionChangeUnk1} {dev->RequestResolutionChangeUnk2} {dev->RequestResolutionChangeUnk3}");
            _dbgLog |= dev->RequestResolutionChange != 0;
            _dbgLog |= dev->RequestResolutionChangeUnk1 != 0;
            _dbgLog |= dev->RequestResolutionChangeUnk2 != 0;
            _dbgLog |= dev->RequestResolutionChangeUnk3 != 0;
        }

        if (_dbgLog)
        {
            Service.PluginLog.Debug(_dbg.ToString());
        }
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

        _dbg.Clear();
        if (Service.DebugConfig.IsDebug)
        {
            _dbg.AppendLine($"STYLE: 0x{style:X8}");
            _dbg.AppendLine($"EXSTYLE: 0x{GetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE):X8}");

            Span<ushort> name = stackalloc ushort[256];
            GetClassName(_currentHwnd, name.GetPointer(0), name.Length);
            WNDCLASSEXW wce;
            GetClassInfoEx(GetModuleHandle(null), name.GetPointer(0), &wce);

            _dbg.AppendLine($"CLASS: {new string((char*) name.GetPointer(0))}");
            _dbg.AppendLine($"CLASS.style: 0x{wce.style:X8}");
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
            _dbg.AppendLine($"NEWSTYLE: 0x{style:X8}");
            _dbg.AppendLine($"NEWEXSTYLE: 0x{exstyle:X8}");
        }

        if (style != styleOrig || exstyle != exstyleOrig || _currentDXVKDWMHackMode != mode)
        {
            if (Service.DebugConfig.IsDebug)
            {
                _dbg.AppendLine("UPDATE");
                _dbgLog = true;
            }

            SetWindowLong(_currentHwnd, GWL.GWL_STYLE, (int) style);
            SetWindowLong(_currentHwnd, GWL.GWL_EXSTYLE, (int) exstyle);

            SetWindowPos(_currentHwnd, HWND.NULL, 0, 0, 0, 0, SWP.SWP_NOZORDER | SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE | SWP.SWP_DRAWFRAME);
            ShowWindow(_currentHwnd, SW.SW_SHOW);
        }
        else if (Service.DebugConfig.IsDebug)
        {
            _dbg.AppendLine("SAME");
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
}

public enum ForceSizeMode
{
    Skip = -1,

    /// <summary>
    /// Fake a window resize event.
    /// </summary>
    FakeWindowResize = 0,

    /// <summary>
    /// Legacy mode, went through more initial testing, works well except for conflicts with other plugins.
    /// Might revert or remove depending on how FakeWindowResize testing / fixups proceed.
    ///
    /// Update the graphics device width / height and set RequestResolutionChange.
    /// This is the same method that other plugins such as SimpleTweaks or XIVWindowResizer use.
    /// </summary>
    LegacyRequestResolutionChange = 1
}
