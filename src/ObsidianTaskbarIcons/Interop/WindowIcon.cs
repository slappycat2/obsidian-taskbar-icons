using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ObsidianTaskbarIcons.Interop;

/// <summary>
/// A small and a big HICON loaded from an .ico file, in the sizes Windows uses for a window's own icon.
/// The handles stay valid only while the process that loaded them is alive: Windows destroys a process's
/// icons when it exits, so whoever pushes these onto another app's window has to stay resident.
/// </summary>
internal sealed class WindowIconPair : IDisposable
{
    public IntPtr Small { get; private set; }
    public IntPtr Big { get; private set; }

    private WindowIconPair(IntPtr small, IntPtr big)
    {
        Small = small;
        Big = big;
    }

    public static WindowIconPair Load(string icoPath)
    {
        var small = LoadAt(icoPath, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        try
        {
            var big = LoadAt(icoPath, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXICON));
            return new WindowIconPair(small, big);
        }
        catch
        {
            NativeMethods.DestroyIcon(small);
            throw;
        }
    }

    private static IntPtr LoadAt(string icoPath, int size)
    {
        var h = NativeMethods.LoadImage(IntPtr.Zero, icoPath, NativeMethods.IMAGE_ICON, size, size,
            NativeMethods.LR_LOADFROMFILE);
        if (h == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not load icon " + icoPath);
        }

        return h;
    }

    public void Dispose()
    {
        if (Small != IntPtr.Zero) NativeMethods.DestroyIcon(Small);
        if (Big != IntPtr.Zero) NativeMethods.DestroyIcon(Big);
        Small = Big = IntPtr.Zero;
    }
}

/// <summary>WM_SETICON / WM_GETICON against another process's window, with a timeout so a hung app cannot stall us.</summary>
internal static class WindowIcon
{
    private const uint TimeoutMs = 1000;

    /// <summary>Sets both icons and returns the handles the window held before (owned by the target process).</summary>
    public static (IntPtr PrevSmall, IntPtr PrevBig) Set(IntPtr hwnd, IntPtr small, IntPtr big)
    {
        var prevSmall = Send(hwnd, NativeMethods.WM_SETICON, NativeMethods.ICON_SMALL, small);
        var prevBig = Send(hwnd, NativeMethods.WM_SETICON, NativeMethods.ICON_BIG, big);
        return (prevSmall, prevBig);
    }

    public static IntPtr Get(IntPtr hwnd, int which) => Send(hwnd, NativeMethods.WM_GETICON, which, IntPtr.Zero);

    private static IntPtr Send(IntPtr hwnd, uint msg, int wParam, IntPtr lParam)
    {
        var ok = NativeMethods.SendMessageTimeout(hwnd, msg, wParam, lParam, NativeMethods.SMTO_ABORTIFHUNG, TimeoutMs,
            out var result);
        if (ok == IntPtr.Zero)
        {
            throw new TimeoutException("The window did not answer within " + TimeoutMs + " ms.");
        }

        return result;
    }
}
