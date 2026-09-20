using System.Runtime.InteropServices;

namespace Icarus.Core.Platform;

/// <summary>POSIX helpers (Linux only, ICARUS-100 decision 5).</summary>
internal static class Posix
{
    /// <summary>
    /// Fully canonicalize <paramref name="path"/> (resolving symlinks in every
    /// component) via <c>realpath(3)</c>, or <c>null</c> when the path does not
    /// exist.
    /// </summary>
    public static string? RealPath(string path)
    {
        var pointer = realpath(path, IntPtr.Zero);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            free(pointer);
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr realpath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc")]
    private static extern void free(IntPtr pointer);
}
