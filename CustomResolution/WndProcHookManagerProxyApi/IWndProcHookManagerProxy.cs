namespace CustomResolution.WndProcHookManagerProxyApi;

public interface IWndProcHookManagerProxy
{
    WndProcHookManager WndProcHookManager { get; }
    object ProxiedValue { get; }
    bool IsValid => WndProcHookManager is not null && ProxiedValue is not null;
}
