using System.Diagnostics;
using ObsidianTaskbarIcons.Interop;

namespace ObsidianTaskbarIcons;

/// <summary>
/// The headless side of the tool: open a vault, wait for its window, stamp its taskbar identity, and make sure
/// the resident <see cref="Watcher"/> is up so the window also carries the profile's icon.
/// </summary>
internal static class Launcher
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan RestampDelay = TimeSpan.FromMilliseconds(1500);

    public sealed record LaunchResult(bool Success, string Message);

    public static LaunchResult Launch(VaultProfile profile)
    {
        var exe = AppPaths.LauncherExe;

        var existing = MatchingWindows(profile);
        if (existing.Count > 0)
        {
            foreach (var w in existing) Stamp(w.Handle, profile, exe);
            Watcher.EnsureRunning();
            WindowIdentity.Activate(existing[0].Handle);
            return new LaunchResult(true, $"Vault '{profile.VaultName}' was already open; window re-tagged and activated.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(ObsidianInfo.OpenVaultUri(profile.VaultName)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, "Could not start Obsidian through its obsidian:// URI handler: " + ex.Message);
        }

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < LaunchTimeout)
        {
            Thread.Sleep(PollInterval);
            var windows = MatchingWindows(profile);
            if (windows.Count == 0) continue;

            foreach (var w in windows) Stamp(w.Handle, profile, exe);

            // Obsidian may still be finishing vault load; apply once more so our identity is the last word.
            Thread.Sleep(RestampDelay);
            foreach (var w in MatchingWindows(profile)) Stamp(w.Handle, profile, exe);
            Watcher.EnsureRunning();

            return new LaunchResult(true, $"Vault '{profile.VaultName}' opened and tagged as {profile.Aumid}.");
        }

        return new LaunchResult(false,
            $"Obsidian did not show a window for vault '{profile.VaultName}' within {LaunchTimeout.TotalSeconds:0} s. " +
            "Make sure the vault is listed in Obsidian's vault switcher, then try again.");
    }

    /// <summary>Stamps every open Obsidian window that belongs to a saved profile. Returns the count tagged.</summary>
    public static int TagAll()
    {
        var exe = AppPaths.LauncherExe;
        var profiles = ProfileStore.Load();
        var tagged = 0;
        foreach (var w in WindowIdentity.FindObsidianWindows())
        {
            var profile = profiles.FirstOrDefault(p => WindowIdentity.MatchesVault(w.Title, p.VaultName));
            if (profile is null) continue;
            Stamp(w.Handle, profile, exe);
            tagged++;
        }

        Watcher.EnsureRunning();
        return tagged;
    }

    public static IEnumerable<string> InspectLines()
    {
        yield return Watcher.IsRunning() ? "Watcher: running" : "Watcher: not running";
        var windows = WindowIdentity.FindObsidianWindows();
        if (windows.Count == 0)
        {
            yield return "No visible Obsidian windows.";
            yield break;
        }

        foreach (var w in windows)
        {
            string aumid;
            try
            {
                aumid = WindowIdentity.GetAppUserModelId(w.Handle) ?? "(none: process default)";
            }
            catch (Exception ex)
            {
                aumid = "(error: " + ex.Message + ")";
            }

            yield return $"0x{w.Handle.ToInt64():X8}  pid={w.ProcessId}  aumid={aumid}  title=\"{w.Title}\"";
        }
    }

    private static List<TopLevelWindow> MatchingWindows(VaultProfile profile) =>
        WindowIdentity.FindObsidianWindows().Where(w => WindowIdentity.MatchesVault(w.Title, profile.VaultName)).ToList();

    private static void Stamp(IntPtr hwnd, VaultProfile profile, string exe)
    {
        try
        {
            WindowIdentity.SetIdentity(hwnd, profile.Aumid, profile.RelaunchCommand(exe), profile.RelaunchIconResource,
                profile.DisplayName);
        }
        catch
        {
            // the window may have closed between enumeration and stamping; the next poll will retry
        }
    }
}

/// <summary>Command-line dispatch: launch / tag / inspect.</summary>
internal static class Cli
{
    private static bool _consoleReady;

    public static int Run(string[] args)
    {
        switch (args[0].ToLowerInvariant())
        {
            case "launch":
            {
                var slug = ArgValue(args, "--id");
                if (slug is null)
                {
                    Fail("Usage: ObsidianTaskbarIcons.exe launch --id <slug>");
                    return 2;
                }

                var profile = ProfileStore.Find(slug);
                if (profile is null)
                {
                    Fail($"No vault profile named '{slug}'. Open Obsidian Taskbar Icons and create it again.");
                    return 3;
                }

                var result = Launcher.Launch(profile);
                if (!result.Success) Fail(result.Message);
                return result.Success ? 0 : 1;
            }
            case "create":
            {
                var vault = ArgValue(args, "--vault");
                if (vault is null)
                {
                    Fail("Usage: ObsidianTaskbarIcons.exe create --vault <folder> [--name <taskbar name>] [--icon <file>] [--no-launch]");
                    return 2;
                }

                try
                {
                    var profile = ProfileBuilder.Create(vault, ArgValue(args, "--name"), ArgValue(args, "--icon"));
                    WriteLine($"Created profile '{profile.Slug}' ({profile.Aumid})");
                    WriteLine("  shortcut: " + profile.ShortcutPath);
                    WriteLine("  icon:     " + profile.IconPath);
                    if (args.Any(a => string.Equals(a, "--no-launch", StringComparison.OrdinalIgnoreCase))) return 0;
                    var result = Launcher.Launch(profile);
                    WriteLine(result.Message);
                    return result.Success ? 0 : 1;
                }
                catch (Exception ex)
                {
                    Fail("Could not create the taskbar icon: " + ex.Message);
                    return 1;
                }
            }
            case "tag":
            {
                var n = Launcher.TagAll();
                WriteLine($"Tagged {n} window(s).");
                return 0;
            }
            case "inspect":
            {
                foreach (var line in Launcher.InspectLines()) WriteLine(line);
                return 0;
            }
            case "watch":
                return Watcher.Run(args.Any(a => string.Equals(a, "--persistent", StringComparison.OrdinalIgnoreCase)));
            default:
                Fail("Commands: create --vault <folder> [--name X] [--icon file] | launch --id <slug> | tag | watch [--persistent] | inspect  (no arguments opens the window)");
                return 2;
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }

        return null;
    }

    private static void WriteLine(string text)
    {
        if (!_consoleReady)
        {
            _consoleReady = true;
            if (!Console.IsOutputRedirected && NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.Out.WriteLine();
            }
        }

        Console.Out.WriteLine(text);
    }

    private static void Fail(string message)
    {
        if (Console.IsOutputRedirected)
        {
            WriteLine(message);
        }
        else
        {
            MessageBox.Show(message, "Obsidian Taskbar Icons", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
