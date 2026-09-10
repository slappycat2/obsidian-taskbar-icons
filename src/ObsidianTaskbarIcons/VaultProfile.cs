using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ObsidianTaskbarIcons;

/// <summary>One vault that has its own taskbar identity.</summary>
internal sealed class VaultProfile
{
    public string Slug { get; set; } = "";
    public string VaultPath { get; set; } = "";
    public string VaultName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Aumid { get; set; } = "";
    public string IconPath { get; set; } = "";
    public string ShortcutPath { get; set; } = "";

    public string RelaunchCommand(string exe) => $"\"{exe}\" launch --id {Slug}";

    [JsonIgnore]
    public string RelaunchIconResource => $"{IconPath},0";
}

internal static class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<VaultProfile> Load()
    {
        if (!File.Exists(AppPaths.ProfilesFile)) return new List<VaultProfile>();
        try
        {
            return JsonSerializer.Deserialize<List<VaultProfile>>(File.ReadAllText(AppPaths.ProfilesFile), JsonOptions)
                   ?? new List<VaultProfile>();
        }
        catch
        {
            return new List<VaultProfile>();
        }
    }

    public static void Save(List<VaultProfile> profiles)
    {
        AppPaths.EnsureDirectories();
        File.WriteAllText(AppPaths.ProfilesFile, JsonSerializer.Serialize(profiles, JsonOptions));
    }

    public static VaultProfile? Find(string slug) =>
        Load().FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static void Upsert(VaultProfile profile)
    {
        var all = Load();
        all.RemoveAll(p => string.Equals(p.Slug, profile.Slug, StringComparison.OrdinalIgnoreCase));
        all.Add(profile);
        Save(all.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public static void Remove(string slug)
    {
        var all = Load();
        all.RemoveAll(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));
        Save(all);
    }

    /// <summary>
    /// Builds a slug that is safe inside an AppUserModelID (no spaces, ASCII only). Two vaults with the
    /// same folder name in different places get a short hash suffix so their identities stay distinct.
    /// </summary>
    public static string MakeSlug(string vaultName, string vaultPath)
    {
        var slug = Regex.Replace(vaultName, "[^A-Za-z0-9._-]+", "_").Trim('_', '.');
        if (slug.Length == 0) slug = "vault";
        if (slug.Length > 60) slug = slug[..60];

        var clash = Load().FirstOrDefault(p =>
            string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFullPath(p.VaultPath), Path.GetFullPath(vaultPath), StringComparison.OrdinalIgnoreCase));
        if (clash is not null)
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(vaultPath).ToUpperInvariant())))[..6];
            slug = $"{slug}-{hash}";
        }

        return slug;
    }

    public static string MakeAumid(string slug) => "Obsidian.Vault." + slug;

    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Vault" : cleaned;
    }
}
