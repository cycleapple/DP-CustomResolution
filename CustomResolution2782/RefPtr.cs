using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace FloppyUtils;

public static unsafe class RefPtr
{
    public static RefPtr<T> For<T>(ref T at) where T : unmanaged
    {
        return new()
        {
            Ref = ref at
        };
    }
}

public unsafe ref struct RefPtr<TAt> where TAt : unmanaged
{
    public ref TAt Ref;

    public TAt* Ptr
    {
        get => (TAt*) Unsafe.AsPointer(ref Ref);
        set => Ref = ref Unsafe.AsRef<TAt>(value);
    }

    public RefPtr<T> Offs<T>(int offs) where T : unmanaged => new()
    {
        Ptr = (T*) ((nint) Ptr + offs)
    };
}
