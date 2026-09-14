using System.Diagnostics;
using Microsoft.Win32;
using ObsidianTaskbarIcons.Interop;

namespace ObsidianTaskbarIcons;

/// <summary>
/// The resident side of the tool. Windows destroys a process's icons when it exits, so a window icon pushed
/// onto Obsidian from a short-lived launcher vanishes the moment the launcher is gone. The watcher stays
/// alive, owns the icon handles, and keeps every open vault window's icon and taskbar identity in sync
/// with its profile. It also picks up windows Obsidian opened on its own, which the launcher never sees.
/// It exits on its own a while after the last Obsidian window closes, unless started with --persistent
/// (which is how the "Start with Windows" entry runs it). A tray icon shows that it is running and offers
/// the few things worth doing to it: open the main window, re-tag now, toggle autostart, exit.
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
    public static int Run(bool persistent)
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew) return 0;

        ApplicationConfiguration.Initialize();
        using var context = new WatcherContext(persistent);
        Application.Run(context);
        return 0;
    }
}

/// <summary>The per-user "Start with Windows" entry for the watcher (HKCU Run key, no admin rights needed).</summary>
internal static class WatcherAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "ObsidianTaskbarIconsWatcher";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{AppPaths.LauncherExe}\" watch --persistent");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
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
    private readonly bool _persistent;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem _trayAutostart = new("Start with Windows") { CheckOnClick = true };
    private List<VaultProfile> _profiles = new();
    private DateTime _profilesStamp = DateTime.MinValue;
    private DateTime _lastSeen = DateTime.UtcNow;
    private int _lastTaggedCount = -1;

    public WatcherContext(bool persistent)
    {
        _persistent = persistent;
        _tray = BuildTrayIcon();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    // ----- tray icon -----------------------------------------------------------------------------

    private NotifyIcon BuildTrayIcon()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Open Obsidian Taskbar Icons", null, (_, _) => OpenMainWindow()) { Font = new Font(menu.Font, FontStyle.Bold) };
        var retag = new ToolStripMenuItem("Re-tag open windows now", null, (_, _) => Retag());
        var exit = new ToolStripMenuItem("Exit watcher", null, (_, _) => ExitThread());

        _trayAutostart.Checked = WatcherAutostart.IsEnabled();
        _trayAutostart.CheckedChanged += (_, _) => ToggleAutostart();
        _trayAutostart.ToolTipText = "Start the watcher at login, so vaults that Obsidian opens on its own get their icons too.";

        menu.Items.AddRange(new ToolStripItem[]
        {
            _trayStatus, new ToolStripSeparator(), open, retag, _trayAutostart, new ToolStripSeparator(), exit,
        });

        var tray = new NotifyIcon
        {
            Icon = AppIcon.Load(16),
            Text = "Obsidian Taskbar Icons",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => OpenMainWindow();
        return tray;
    }

    private void UpdateTrayText(int tagged)
    {
        if (tagged == _lastTaggedCount) return;
        _lastTaggedCount = tagged;
        var summary = tagged switch
        {
            0 => "no vault windows open",
            1 => "keeping 1 vault window tagged",
            _ => $"keeping {tagged} vault windows tagged",
        };
        _trayStatus.Text = "Watcher: " + summary + (_persistent ? "" : " (exits 5 min after the last one closes)");
        // NotifyIcon.Text is limited to 127 characters
        _tray.Text = "Obsidian Taskbar Icons: " + summary;
    }

    private void OpenMainWindow()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(_exe) ?? "" });
        }
        catch
        {
            // nothing sensible to do from the tray
        }
    }

    private void Retag()
    {
        // Launcher.TagAll stamps the identity; the tick that follows puts the icons back on top.
        var n = Launcher.TagAll();
        Tick();
        _tray.ShowBalloonTip(3000, "Obsidian Taskbar Icons",
            n == 0 ? "No open Obsidian window matched a saved taskbar icon." : $"Re-tagged {n} window(s).", ToolTipIcon.Info);
    }

    private void ToggleAutostart()
    {
        try
        {
            WatcherAutostart.Set(_trayAutostart.Checked);
        }
        catch (Exception ex)
        {
            _trayAutostart.Checked = WatcherAutostart.IsEnabled();
            _tray.ShowBalloonTip(4000, "Obsidian Taskbar Icons", "Could not change the startup setting: " + ex.Message, ToolTipIcon.Warning);
        }
    }

    // ----- the loop -------------------------------------------------------------------------------

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
            else if (!_persistent && now - _lastSeen > IdleExit)
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

            UpdateTrayText(_windows.Count);
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
            _tray.Visible = false;
            _tray.Dispose();
            foreach (var hwnd in _windows.Keys.ToList()) Forget(hwnd, restore: true);
            foreach (var pair in _icons.Values) pair.Dispose();
            _icons.Clear();
        }

        base.Dispose(disposing);
    }
}
