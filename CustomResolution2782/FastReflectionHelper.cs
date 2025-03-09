using System;
using System.Reflection;
using System.Reflection.Emit;

namespace MonoMod.Utils;

public delegate object? FastReflectionDelegate(object? target, params object?[] args);

public static class FastReflectionHelper
{
    private static readonly Type[] _dynamicMethodDelegateArgs = { typeof(object), typeof(object?[]) };

    public static FastReflectionDelegate CreateFastDelegate(MethodBase method, bool directBoxValueAccess = true)
    {
        var dm = new DynamicMethod($"FastReflection<{method}>", typeof(object), _dynamicMethodDelegateArgs);
        var il = dm.GetILGenerator();

        var args = method.GetParameters();

        var generateLocalBoxValuePtr = true;

        if (!method.IsStatic)
        {
            il.Emit(OpCodes.Ldarg_0);

            if (method.DeclaringType?.IsValueType ?? false)
            {
                il.Emit(OpCodes.Unbox_Any, method.DeclaringType);
            }
        }

        for (var i = 0; i < args.Length; i++)
        {
            var argType = args[i].ParameterType;
            var argIsByRef = argType.IsByRef;

            if (argIsByRef)
            {
                argType = argType.GetElementType()!;
            }

            var argIsValueType = argType.IsValueType;

            if (argIsByRef && argIsValueType && !directBoxValueAccess)
            {
                // Used later when storing back the reference to the new box in the array.
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, i);
            }

            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, i);

            if (argIsByRef && !argIsValueType)
            {
                il.Emit(OpCodes.Ldelema, typeof(object));
            }
            else
            {
                il.Emit(OpCodes.Ldelem_Ref);

                if (argIsValueType)
                {
                    if (!argIsByRef || !directBoxValueAccess)
                    {
                        // if !directBoxValueAccess, create a new box if required
                        il.Emit(OpCodes.Unbox_Any, argType);

                        if (argIsByRef)
                        {
                            // box back
                            il.Emit(OpCodes.Box, argType);

                            // store new box value address to local 0
                            il.Emit(OpCodes.Dup);
                            il.Emit(OpCodes.Unbox, argType);

                            if (generateLocalBoxValuePtr)
                            {
                                generateLocalBoxValuePtr = false;
                                il.DeclareLocal(typeof(void*));
                            }

                            il.Emit(OpCodes.Stloc_0);

                            // arr and index set up already
                            il.Emit(OpCodes.Stelem_Ref);

                            // load address back to stack
                            il.Emit(OpCodes.Ldloc_0);
                        }
                    }
                    else
                    {
                        // if directBoxValueAccess, emit unbox (get value address)
                        il.Emit(OpCodes.Unbox, argType);
                    }
                }
            }
        }

        if (method.IsConstructor)
        {
            il.Emit(OpCodes.Newobj, (ConstructorInfo) method);
        }
        else if (method.IsFinal || !method.IsVirtual)
        {
            il.Emit(OpCodes.Call, (MethodInfo) method);
        }
        else
        {
            il.Emit(OpCodes.Callvirt, (MethodInfo) method);
        }

        var returnType = method.IsConstructor ? method.DeclaringType! : ((MethodInfo) method).ReturnType;

        if (returnType != typeof(void))
        {
            if (returnType.IsValueType)
            {
                il.Emit(OpCodes.Box, returnType);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Ret);

        return (FastReflectionDelegate) dm.CreateDelegate(typeof(FastReflectionDelegate));
    }
}
