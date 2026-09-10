using ObsidianTaskbarIcons.Interop;

namespace ObsidianTaskbarIcons;

/// <summary>Shared by the window and the CLI: turns a vault folder + name + icon into a profile, icon file and shortcut.</summary>
internal static class ProfileBuilder
{
    public static VaultProfile Create(string vaultPath, string? displayName, string? iconSource)
    {
        vaultPath = Path.GetFullPath(vaultPath.Trim().Trim('"'));
        if (!Directory.Exists(vaultPath))
        {
            throw new DirectoryNotFoundException("Vault folder not found: " + vaultPath);
        }

        var vaultName = ObsidianInfo.VaultName(vaultPath);
        displayName = string.IsNullOrWhiteSpace(displayName) ? vaultName : displayName.Trim();

        iconSource ??= ObsidianInfo.FindObsidianExe();
        if (iconSource is null || !File.Exists(iconSource))
        {
            throw new FileNotFoundException("Icon source not found: " + (iconSource ?? "(Obsidian.exe)"));
        }

        if (!IconConverter.IsSupported(iconSource))
        {
            throw new InvalidOperationException("Unsupported icon file type: " + Path.GetExtension(iconSource));
        }

        var slug = ProfileStore.MakeSlug(vaultName, vaultPath);
        var previous = ProfileStore.Find(slug);
        if (previous is not null && File.Exists(previous.ShortcutPath))
        {
            try { File.Delete(previous.ShortcutPath); } catch { /* replaced below anyway */ }
        }

        AppPaths.EnsureDirectories();
        var icoPath = Path.Combine(AppPaths.Icons, slug + ".ico");
        IconConverter.ToIcoFile(iconSource, icoPath);

        var profile = new VaultProfile
        {
            Slug = slug,
            VaultPath = vaultPath,
            VaultName = vaultName,
            DisplayName = displayName,
            Aumid = ProfileStore.MakeAumid(slug),
            IconPath = icoPath,
            ShortcutPath = Path.Combine(AppPaths.StartMenuPrograms,
                "Obsidian - " + ProfileStore.SafeFileName(displayName) + ".lnk"),
        };

        var exe = AppPaths.LauncherExe;
        ShellLink.Create(profile.ShortcutPath, exe, $"launch --id {profile.Slug}", Path.GetDirectoryName(exe)!,
            profile.IconPath, 0, $"Open the Obsidian vault {profile.VaultName}", profile.Aumid);
        ProfileStore.Upsert(profile);
        return profile;
    }
}
