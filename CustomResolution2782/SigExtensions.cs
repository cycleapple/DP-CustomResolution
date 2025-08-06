using Dalamud.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CustomResolution;

public struct RVASig
{
    public string Full;
    public string Text;
    public int Start;
    public int Addr;
    public int End;

    public RVASig(string text)
    {
        Full = text;
        text = text.Replace("|", "");

        var start = text.IndexOf('(');
        var addr = text.IndexOf('*');
        var end = text.IndexOf(')');
        if (start == -1 || addr == -1 || end == -1)
        {
            throw new ArgumentException($"Invalid signature, must be of format \"AA (BB *CC) DD\", but got: {Full}");
        }

        Text = string.Concat(text.AsSpan(0, start), text.AsSpan(start + 1, addr - (start + 1)), text.AsSpan(addr + 1, end - (addr + 1)), text.AsSpan(end + 1));
        Start = start / 3;
        Addr = (addr - 1) / 3;
        End = (end - 2) / 3 + 1;

        if ((End - Addr) != 4)
        {
            throw new ArgumentException($"Invalid signature, RVA must be sizeof(int) == 4, but got {End - Addr}: {Full}");
        }
    }
}

public struct MultiRVASig
{
    public string Full;
    public RVASig[] Sigs;

    public MultiRVASig(string text)
    {
        Full = text;
        text = text.Replace("|", "");

        List<RVASig> sigs = [];

        do
        {
            var start = text.IndexOf('(');
            if (start == -1)
            {
                break;
            }

            var end = text.IndexOf(')');
            sigs.Add(new(text.Substring(0, end + 1) + text.Substring(end + 1).Replace("(", "").Replace("*", "").Replace(")", "")));

            text = text.Substring(0, end).Replace("(", "").Replace("*", "") + text.Substring(end + 1);
        } while (true);

        Sigs = sigs.ToArray();
    }
}

public static unsafe class SigExtensions
{
    public static nint ScanRVASig(this ISigScanner scanner, in RVASig sig)
    {
        var code = scanner.ScanText(sig.Text);
        var rva = *(int*) (code + sig.Addr);
        var addr = Service.SigScanner.ResolveRelativeAddress(code + sig.End, rva);
        Service.PluginLog.Debug($"ScanRVASig({sig.Text}) -> 0x{(long) code:X16} + {sig.Start} + {sig.Addr - sig.Start} + {sig.End - sig.Addr} + 0x{rva:X8} -> 0x{(long) addr:X16}");
        return addr;
    }

    public static nint Verify(this nint ptr, nint other)
    {
        if (ptr != other)
        {
            throw new ArgumentException($"Pointers do not match: 0x{(long) ptr:X16} vs 0x{(long) ptr:X16}");
        }

        return ptr;
    }

    public static nint Log(this nint ptr, [CallerArgumentExpression(nameof(ptr))] string? ctx = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ctx);
        Service.PluginLog.Debug($"{ctx} -> 0x{(long) ptr:X16}");
        return ptr;
    }
}
