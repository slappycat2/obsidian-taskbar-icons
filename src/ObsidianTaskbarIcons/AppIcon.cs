using System.Reflection;

namespace ObsidianTaskbarIcons;

/// <summary>
/// The tool's own icon (the red gem from samples\icons), embedded as a resource so the window, the tray
/// and the installer all show the same picture regardless of where the exe is running from.
/// </summary>
internal static class AppIcon
{
    private const string ResourceName = "app.ico";

    /// <summary>Loads the embedded icon at the requested size. Falls back to the exe's associated icon.</summary>
    public static Icon Load(int size)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is not null) return new Icon(stream, size, size);
        }
        catch
        {
            // fall through to the exe resource
        }

        var exe = Environment.ProcessPath;
        return (exe is not null ? Icon.ExtractAssociatedIcon(exe) : null) ?? SystemIcons.Application;
    }

    public static string Version
    {
        get
        {
            var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info)) info = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }
}
