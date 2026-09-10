using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ObsidianTaskbarIcons;

internal sealed record KnownVault(string Path, bool Open)
{
    public string Name => ObsidianInfo.VaultName(Path);
}

/// <summary>Reads what Obsidian itself knows: where its exe is and which vaults are registered.</summary>
internal static class ObsidianInfo
{
    public static string ConfigFile { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");

    public static string? FindObsidianExe()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\obsidian\shell\open\command");
            if (key?.GetValue(null) is string cmd)
            {
                var m = Regex.Match(cmd, "^\"([^\"]+)\"");
                var exe = m.Success ? m.Groups[1].Value : cmd.Split(' ')[0];
                if (File.Exists(exe)) return exe;
            }
        }
        catch
        {
            // fall through to the default location
        }

        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Obsidian", "Obsidian.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    public static List<KnownVault> KnownVaults()
    {
        var result = new List<KnownVault>();
        if (!File.Exists(ConfigFile)) return result;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
            if (!doc.RootElement.TryGetProperty("vaults", out var vaults)) return result;
            foreach (var v in vaults.EnumerateObject())
            {
                if (!v.Value.TryGetProperty("path", out var p) || p.GetString() is not { } path) continue;
                var open = v.Value.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
                result.Add(new KnownVault(path, open));
            }
        }
        catch
        {
            // a half-written config while Obsidian is saving is not fatal
        }

        return result.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string VaultName(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

    public static bool LooksLikeVault(string path) =>
        Directory.Exists(path) && Directory.Exists(Path.Combine(path, ".obsidian"));

    public static bool IsRegistered(string path)
    {
        var full = Normalize(path);
        return KnownVaults().Any(v => Normalize(v.Path) == full);
    }

    /// <summary>obsidian://open?vault=NAME is the form that works reliably on Windows for registered vaults.</summary>
    public static string OpenVaultUri(string vaultName) => "obsidian://open?vault=" + Uri.EscapeDataString(vaultName);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
}
