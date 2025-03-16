using TerraFX.Interop.Windows;

namespace CustomResolution.WndProcHookManagerProxyApi;

// This could be optimized in a thousand different ways (better dynamic methods, source-gen this class), but eh.

public readonly record struct WndProcEventArgs(WndProcHookManager WndProcHookManager, object ProxiedValue) : IWndProcHookManagerProxy
{
    public HWND Hwnd => (HWND) WndProcHookManager.GetValue(ProxiedValue, nameof(Hwnd))!;
    public int ViewportId => (int) WndProcHookManager.GetValue(ProxiedValue, nameof(ViewportId))!;

    public uint Message
    {
        get => (uint) WndProcHookManager.GetValue(ProxiedValue, nameof(Message))!;
        set => WndProcHookManager.SetValue(ProxiedValue, nameof(Message), value);
    }

    public WPARAM WParam
    {
        get => (WPARAM) WndProcHookManager.GetValue(ProxiedValue, nameof(WParam))!;
        set => WndProcHookManager.SetValue(ProxiedValue, nameof(WParam), value);
    }

    public LPARAM LParam
    {
        get => (LPARAM) WndProcHookManager.GetValue(ProxiedValue, nameof(LParam))!;
        set => WndProcHookManager.SetValue(ProxiedValue, nameof(LParam), value);
    }

    public bool SuppressCall
    {
        get => (bool) WndProcHookManager.GetValue(ProxiedValue, nameof(SuppressCall))!;
        set => WndProcHookManager.SetValue(ProxiedValue, nameof(SuppressCall), value);
    }

    public LRESULT ReturnValue
    {
        get => (LRESULT) WndProcHookManager.GetValue(ProxiedValue, nameof(ReturnValue))!;
        set => WndProcHookManager.SetValue(ProxiedValue, nameof(ReturnValue), value);
    }
}
