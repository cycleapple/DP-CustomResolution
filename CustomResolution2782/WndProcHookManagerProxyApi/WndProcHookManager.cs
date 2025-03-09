using Dalamud.IoC;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CustomResolution.WndProcHookManagerProxyApi;

public class WndProcHookManager
{
    internal const BindingFlags _BindingFlags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

    private object? _realManager;
    private Type? _realManagerType;

    private WeakReference? _lastManager;
    private readonly Dictionary<(Type, string), FastReflectionDelegate> _methodCache = new();
    private readonly Dictionary<(Type, string), MemberInfo> _memberCache = new();

    public WndProcHookManager()
    {
        Refresh();
    }

    public bool IsAvailable => _realManager is not null;
    public object? ProxiedValue => _realManager;

    public EventInfo? PreWndProc { get; private set; }

    public bool Refresh()
    {
        if (IsAvailable)
        {
            return true;
        }

        Service.PluginLog.Information("Refreshing WndProcHookManagerProxyApi");

        try
        {
            if (typeof(PluginServiceAttribute).Assembly.GetType("Dalamud.Service`1") is not { } serviceContainerContainer)
            {
                Service.PluginLog.Warning("CustomResolution couldn't find the service container types.");

                Unset();

                return false;
            }

            if (typeof(PluginServiceAttribute).Assembly.GetType("Dalamud.Hooking.WndProcHook.WndProcHookManager") is not { } wndProcHookManagerType)
            {
                Service.PluginLog.Warning("CustomResolution couldn't find the WndProcHookManager type.");

                Unset();

                return false;
            }

            _realManagerType = wndProcHookManagerType;

            serviceContainerContainer = serviceContainerContainer.MakeGenericType(wndProcHookManagerType);

            if (serviceContainerContainer.GetMethod("Get")?.Invoke(null, Array.Empty<object>()) is not object manager)
            {
                Service.PluginLog.Warning("CustomResolution couldn't obtain the WndProcHookManager instance.");

                Unset();

                return false;
            }

            _realManager = manager;
        }
        catch (Exception e)
        {
            Service.PluginLog.Warning($"CustomResolution couldn't obtain the WndProcHookManager service: {e}");

            Unset();

            return false;
        }


        Service.PluginLog.Information($"WndProcHookManager found: {_realManagerType.Assembly.FullName}");

        if (_lastManager?.Target != _realManager)
        {
            Service.PluginLog.Information($"Different WndProcHookManager, clearing cache");

            ClearCache();

            _lastManager = new WeakReference(_realManager);
        }

        PreWndProc = _realManagerType.GetEvent("PreWndProc");

        return true;
    }

    internal FastReflectionDelegate GetMethod(Type target, string name, Func<Type, MethodBase>? getMethod = null)
    {
        if (_methodCache.TryGetValue((target, name), out var fun))
        {
            return fun;
        }

        return _methodCache[(target, name)] = FastReflectionHelper.CreateFastDelegate(getMethod?.Invoke(target) ?? target.GetMethods().FirstOrDefault(m => m.Name == name) ?? target.GetMethod(name, _BindingFlags)!);
    }

    internal object? InvokeStatic(string typeName, string name, params object?[] args)
    {
        if (_realManagerType?.Assembly.GetType(typeName) is not { } type)
        {
            Service.PluginLog.Information($"GetStaticValue failed, type \"{typeName}\" not found");

            return null;
        }

        return GetMethod(type, name).Invoke(null, args);
    }

    internal object? Invoke(object proxiedValue, string name, params object?[] args)
    {
        return GetMethod(proxiedValue.GetType(), name).Invoke(proxiedValue, args);
    }

    internal object? GetStaticValue(string typeName, string name, params object?[] args)
    {
        if (_realManagerType?.Assembly.GetType(typeName) is not { } type)
        {
            Service.PluginLog.Information($"GetStaticValue failed, type \"{typeName}\" not found");

            return null;
        }

        if (!_memberCache.TryGetValue((type, name), out var member))
        {
            member = _memberCache[(type, name)] = type.GetMember(name, _BindingFlags).First();
        }

        if (member is FieldInfo field)
        {
            return field.GetValue(null);
        }

        if (member is PropertyInfo property)
        {
            return property.GetValue(null, args);
        }

        return InvokeStatic(typeName, name, args);
    }

    internal object? GetValue(object proxiedValue, string name, params object?[] args)
    {
        if (!_memberCache.TryGetValue((proxiedValue.GetType(), name), out var member))
        {
            member = _memberCache[(proxiedValue.GetType(), name)] = proxiedValue.GetType().GetMember(name, _BindingFlags).First();
        }

        if (member is FieldInfo field)
        {
            return field.GetValue(proxiedValue);
        }

        if (member is PropertyInfo property)
        {
            return property.GetValue(proxiedValue, args);
        }

        return Invoke(proxiedValue, name, args);
    }

    internal void SetStaticValue(string typeName, string name, params object?[] args)
    {
        if (_realManagerType?.Assembly.GetType(typeName) is not { } type)
        {
            Service.PluginLog.Information($"GetStaticValue failed, type \"{typeName}\" not found");

            return;
        }

        if (!_memberCache.TryGetValue((type, name), out var member))
        {
            member = _memberCache[(type, name)] = type.GetMember(name, _BindingFlags).First();
        }

        if (member is FieldInfo field)
        {
            field.SetValue(null, args[0]);

            return;
        }

        if (member is PropertyInfo property)
        {
            property.SetValue(null, args.First(), args.Length > 1 ? args.Skip(1).ToArray() : null);

            return;
        }

        InvokeStatic(typeName, name, args);
    }

    internal void SetValue(object proxiedValue, string name, params object?[] args)
    {
        if (!_memberCache.TryGetValue((proxiedValue.GetType(), name), out var member))
        {
            member = _memberCache[(proxiedValue.GetType(), name)] = proxiedValue.GetType().GetMember(name, _BindingFlags).First();
        }

        if (member is FieldInfo field)
        {
            field.SetValue(proxiedValue, args[0]);

            return;
        }

        if (member is PropertyInfo property)
        {
            property.SetValue(proxiedValue, args.First(), args.Length > 1 ? args.Skip(1).ToArray() : null);

            return;
        }

        Invoke(proxiedValue, name, args);
    }

    internal ITuple ConvertValueTuple(Type targetType, object proxiedValue)
    {
        var rawTuple = (ITuple) proxiedValue;
        var dstItems = new object?[rawTuple.Length];
        var rawTypes = proxiedValue.GetType().GenericTypeArguments;
        var dstTypes = targetType.GenericTypeArguments;

        for (var i = 0; i < rawTuple.Length; i++)
        {
            var raw = rawTuple[i];

            if (dstTypes[i].IsAssignableFrom(rawTypes[i]))
            {
                dstItems[i] = raw;
            }
            else if (raw is not null)
            {
                var dstType = dstTypes[i];

                var isNullable = dstType.FullName?.StartsWith("System.Nullable`1[[") ?? false;

                if (isNullable)
                {
                    dstType = dstType.GenericTypeArguments[0];
                }

                dstItems[i] = Activator.CreateInstance(dstType, new object[] { this, raw });

                if (isNullable)
                {
                    dstItems[i] = Activator.CreateInstance(dstTypes[i], new object[] { dstItems[i]! });
                }
            }
            else
            {
                dstItems[i] = null;
            }
        }

        return (ITuple) Activator.CreateInstance(targetType, dstItems)!;
    }

    private void Unset()
    {
        _realManager = default;
        _realManagerType = default;

        PreWndProc = default;

        ClearCache();
    }

    private void ClearCache()
    {
        _lastManager = null;
        _methodCache.Clear();
        _memberCache.Clear();
    }
}
