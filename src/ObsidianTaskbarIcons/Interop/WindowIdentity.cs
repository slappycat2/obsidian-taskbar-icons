using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ObsidianTaskbarIcons.Interop;

internal sealed record TopLevelWindow(IntPtr Handle, string Title, uint ProcessId);

/// <summary>
/// Finds Obsidian's top-level windows and stamps taskbar identity properties on them from the outside.
/// An explicit per-window System.AppUserModel.ID overrides the process-wide one Electron sets, so the
/// taskbar regroups the window immediately.
/// </summary>
internal static class WindowIdentity
{
    private static readonly Guid IID_IPropertyStore = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");

    // "Note - Vault - Obsidian", "Vault - Obsidian", and since 1.14 "Note - Vault - Obsidian 1.14.1".
    private static readonly Regex TitlePattern =
        new(@"^(?<core>.+?)\s-\sObsidian(?:\s[\w.\-]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<TopLevelWindow> FindObsidianWindows()
    {
        var found = new List<TopLevelWindow>();
        var processNames = new Dictionary<uint, string>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;

            var len = NativeMethods.GetWindowTextLength(hwnd);
            if (len == 0) return true;
            var sb = new StringBuilder(len + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (!processNames.TryGetValue(pid, out var name))
            {
                try
                {
                    name = Process.GetProcessById((int)pid).ProcessName;
                }
                catch
                {
                    name = "";
                }

                processNames[pid] = name;
            }

            if (string.Equals(name, "Obsidian", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new TopLevelWindow(hwnd, sb.ToString(), pid));
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>True when the window title belongs to the given vault. Dash variants are normalised.</summary>
    public static bool MatchesVault(string title, string vaultName)
    {
        var t = title.Replace('–', '-').Replace('—', '-').Trim();
        var m = TitlePattern.Match(t);
        if (!m.Success) return false;
        var core = m.Groups["core"].Value;
        return string.Equals(core, vaultName, StringComparison.OrdinalIgnoreCase)
               || core.EndsWith(" - " + vaultName, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetIdentity(IntPtr hwnd, string aumid, string relaunchCommand, string relaunchIcon,
        string displayName)
    {
        var store = OpenStore(hwnd);
        try
        {
            store.SetString(AppUserModelKeys.Id, aumid);
            store.SetString(AppUserModelKeys.RelaunchCommand, relaunchCommand);
            store.SetString(AppUserModelKeys.RelaunchIconResource, relaunchIcon);
            store.SetString(AppUserModelKeys.RelaunchDisplayNameResource, displayName);
            store.Commit();
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public static string? GetAppUserModelId(IntPtr hwnd)
    {
        var store = OpenStore(hwnd);
        try
        {
            return store.GetString(AppUserModelKeys.Id);
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    public static void Activate(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        NativeMethods.SetForegroundWindow(hwnd);
    }

    private static IPropertyStore OpenStore(IntPtr hwnd)
    {
        var iid = IID_IPropertyStore;
        var hr = NativeMethods.SHGetPropertyStoreForWindow(hwnd, ref iid, out var store);
        if (hr != 0)
        {
            Marshal.ThrowExceptionForHR(hr);
        }

        return store;
    }
}
