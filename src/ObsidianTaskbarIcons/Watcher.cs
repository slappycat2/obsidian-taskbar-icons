using System.Diagnostics;
using ObsidianTaskbarIcons.Interop;

namespace ObsidianTaskbarIcons;

/// <summary>
/// The resident side of the tool. Windows destroys a process's icons when it exits, so a window icon pushed
/// onto Obsidian from a short-lived launcher vanishes the moment the launcher is gone. The watcher stays
/// alive, owns the icon handles, and keeps every open vault window's icon and taskbar identity in sync
/// with its profile. It also picks up windows Obsidian opened on its own, which the launcher never sees.
/// It exits on its own a while after the last Obsidian window closes.
/// </summary>
internal static class Watcher
{
    private const string MutexName = @"Local\ObsidianTaskbarIcons.Watcher";

    public static bool IsRunning()
    {
        if (!Mutex.TryOpenExisting(MutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    /// <summary>Starts a detached watcher unless one is already running. Never throws.</summary>
    public static void EnsureRunning()
    {
        try
        {
            if (IsRunning()) return;
            Process.Start(new ProcessStartInfo(AppPaths.LauncherExe, "watch")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(AppPaths.LauncherExe) ?? "",
            });
        }
        catch
        {
            // the taskbar grouping still works without it; only the window icon is lost
        }
    }

    /// <summary>Runs the watcher on the current (STA) thread until it decides to exit. Returns immediately if one runs already.</summary>
    public static int Run()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return 0;

        ApplicationConfiguration.Initialize();
        using var context = new WatcherContext();
        Application.Run(context);
        return 0;
    }
}

internal sealed class WatcherContext : ApplicationContext
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IdleExit = TimeSpan.FromMinutes(5);

    private sealed class TaggedWindow
    {
        public required string Slug;
        public required WindowIconPair Icons;
        public IntPtr PrevSmall;
        public IntPtr PrevBig;
    }

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = (int)Poll.TotalMilliseconds };
    private readonly Dictionary<IntPtr, TaggedWindow> _windows = new();
    private readonly Dictionary<string, WindowIconPair> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _exe = AppPaths.LauncherExe;
    private List<VaultProfile> _profiles = new();
    private DateTime _profilesStamp = DateTime.MinValue;
    private DateTime _lastSeen = DateTime.UtcNow;

    public WatcherContext()
    {
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private void Tick()
    {
        try
        {
            ReloadProfilesIfChanged();
            var windows = WindowIdentity.FindObsidianWindows();
            var now = DateTime.UtcNow;
            if (windows.Count > 0)
            {
                _lastSeen = now;
            }
            else if (now - _lastSeen > IdleExit)
            {
                ExitThread();
                return;
            }

            var seen = new HashSet<IntPtr>();
            foreach (var w in windows)
            {
                seen.Add(w.Handle);
                var profile = _profiles.FirstOrDefault(p => WindowIdentity.MatchesVault(w.Title, p.VaultName));
                if (profile is null)
                {
                    Forget(w.Handle, restore: true);
                    continue;
                }

                Apply(w.Handle, profile);
            }

            foreach (var hwnd in _windows.Keys.Where(h => !seen.Contains(h) && !NativeMethods.IsWindow(h)).ToList())
            {
                Forget(hwnd, restore: false);
            }
        }
        catch
        {
            // a bad tick (window vanished mid-way, profiles.json half written) is retried next second
        }
    }

    private void Apply(IntPtr hwnd, VaultProfile profile)
    {
        var icons = IconsFor(profile);
        if (icons is null) return;

        if (_windows.TryGetValue(hwnd, out var state) && (state.Slug != profile.Slug || state.Icons != icons))
        {
            // the window switched vault, or the profile's icon file was replaced
            Forget(hwnd, restore: true);
            state = null;
        }

        try
        {
            if (state is null)
            {
                state = new TaggedWindow { Slug = profile.Slug, Icons = icons };
                (state.PrevSmall, state.PrevBig) = WindowIcon.Set(hwnd, icons.Small, icons.Big);
                _windows[hwnd] = state;
            }
            else if (WindowIcon.Get(hwnd, NativeMethods.ICON_SMALL) != icons.Small ||
                     WindowIcon.Get(hwnd, NativeMethods.ICON_BIG) != icons.Big)
            {
                // Obsidian set its own icon again; ours goes back on top
                WindowIcon.Set(hwnd, icons.Small, icons.Big);
            }

            if (!string.Equals(WindowIdentity.GetAppUserModelId(hwnd), profile.Aumid, StringComparison.Ordinal))
            {
                WindowIdentity.SetIdentity(hwnd, profile.Aumid, profile.RelaunchCommand(_exe), profile.RelaunchIconResource,
                    profile.DisplayName);
            }
        }
        catch
        {
            // the window is closing or not answering; next tick decides
        }
    }

    private void Forget(IntPtr hwnd, bool restore)
    {
        if (!_windows.Remove(hwnd, out var state)) return;
        if (!restore || !NativeMethods.IsWindow(hwnd)) return;
        try
        {
            WindowIcon.Set(hwnd, state.PrevSmall, state.PrevBig);
        }
        catch
        {
            // best effort
        }
    }

    private WindowIconPair? IconsFor(VaultProfile profile)
    {
        if (!File.Exists(profile.IconPath)) return null;
        var key = profile.IconPath + "|" + File.GetLastWriteTimeUtc(profile.IconPath).Ticks;
        if (_icons.TryGetValue(key, out var pair)) return pair;
        try
        {
            pair = WindowIconPair.Load(profile.IconPath);
        }
        catch
        {
            return null;
        }

        _icons[key] = pair;
        return pair;
    }

    private void ReloadProfilesIfChanged()
    {
        var stamp = File.Exists(AppPaths.ProfilesFile) ? File.GetLastWriteTimeUtc(AppPaths.ProfilesFile) : DateTime.MinValue;
        if (stamp == _profilesStamp) return;
        _profilesStamp = stamp;
        _profiles = ProfileStore.Load();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (var hwnd in _windows.Keys.ToList()) Forget(hwnd, restore: true);
            foreach (var pair in _icons.Values) pair.Dispose();
            _icons.Clear();
        }

        base.Dispose(disposing);
    }
}
