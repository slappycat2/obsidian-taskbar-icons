namespace ObsidianTaskbarIcons;

/// <summary>Stable on-disk locations used by the tool.</summary>
internal static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObsidianTaskbarIcons");

    public static string Bin { get; } = Path.Combine(Root, "bin");
    public static string Icons { get; } = Path.Combine(Root, "icons");
    public static string InstalledSamples { get; } = Path.Combine(Root, "samples");
    public static string ProfilesFile { get; } = Path.Combine(Root, "profiles.json");
    public static string InstalledExe { get; } = Path.Combine(Bin, "ObsidianTaskbarIcons.exe");

    public static string StartMenuPrograms { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");

    /// <summary>
    /// The exe that shortcuts and relaunch commands should point at. Prefers the installed copy so
    /// that rebuilding the project never breaks an existing pin.
    /// </summary>
    public static string LauncherExe =>
        File.Exists(InstalledExe) ? InstalledExe : Environment.ProcessPath ?? InstalledExe;

    /// <summary>
    /// The folder of sample icons shipped with the tool: the installed copy, or the repo's samples\icons when
    /// running straight from a build output. Null if neither exists.
    /// </summary>
    public static string? SampleIcons
    {
        get
        {
            if (Directory.Exists(InstalledSamples)) return InstalledSamples;
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            for (var i = 0; i < 6 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir, "samples", "icons");
                if (Directory.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            return null;
        }
    }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Icons);
    }
}
