using System.Runtime.CompilerServices;

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
