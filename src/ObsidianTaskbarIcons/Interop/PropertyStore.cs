using System.Runtime.InteropServices;

namespace ObsidianTaskbarIcons.Interop;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId)
    {
        FormatId = formatId;
        PropertyId = propertyId;
    }
}

/// <summary>Minimal PROPVARIANT: only VT_LPWSTR and VT_EMPTY are ever used here.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort VarType;
    public ushort Reserved1;
    public ushort Reserved2;
    public ushort Reserved3;
    public IntPtr Pointer;
    public IntPtr Pointer2;

    public const ushort VT_EMPTY = 0;
    public const ushort VT_LPWSTR = 31;

    public static PropVariant FromString(string value) => new()
    {
        VarType = VT_LPWSTR,
        Pointer = Marshal.StringToCoTaskMemUni(value),
    };

    public string? AsString() => VarType == VT_LPWSTR && Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(Pointer) : null;

    public void Clear() => NativeMethods.PropVariantClear(ref this);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);
    void GetAt(uint index, out PropertyKey key);
    void GetValue(ref PropertyKey key, out PropVariant value);
    void SetValue(ref PropertyKey key, ref PropVariant value);
    void Commit();
}

/// <summary>Well-known System.AppUserModel.* property keys (FMTID 9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3).</summary>
internal static class AppUserModelKeys
{
    private static readonly Guid FormatId = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static PropertyKey RelaunchCommand => new(FormatId, 2);
    public static PropertyKey RelaunchIconResource => new(FormatId, 3);
    public static PropertyKey RelaunchDisplayNameResource => new(FormatId, 4);
    public static PropertyKey Id => new(FormatId, 5);
}

internal static class PropertyStoreExtensions
{
    public static void SetString(this IPropertyStore store, PropertyKey key, string value)
    {
        var pv = PropVariant.FromString(value);
        try
        {
            store.SetValue(ref key, ref pv);
        }
        finally
        {
            pv.Clear();
        }
    }

    public static string? GetString(this IPropertyStore store, PropertyKey key)
    {
        store.GetValue(ref key, out var pv);
        try
        {
            return pv.AsString();
        }
        finally
        {
            pv.Clear();
        }
    }
}
