using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ValheimRadar
{
    // Minimal P/Invoke surface over the native SQLite library ("e_sqlite3", SQLite built and packaged
    // by SQLitePCLRaw.lib.e_sqlite3 - see ValheimRadar.csproj). Valheim ships no SQLite and no
    // ADO.NET provider, and a managed wrapper would drag in more assemblies than the handful of
    // calls PinDatabase needs. Strings cross the boundary as UTF-8 byte arrays (see SqliteConnection).
    internal static class SqliteNative
    {
        private const string Lib = "e_sqlite3";

        internal const int SQLITE_OK = 0;
        internal const int SQLITE_ROW = 100;
        internal const int SQLITE_DONE = 101;
        internal const int SQLITE_NULL = 5;

        internal const int SQLITE_OPEN_READWRITE = 0x00000002;
        internal const int SQLITE_OPEN_CREATE = 0x00000004;

        // Tells sqlite3_bind_text to copy the buffer, so the managed array can be collected right away.
        internal static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_libversion();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_close_v2(IntPtr db);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int nByte, out IntPtr stmt, IntPtr tail);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_step(IntPtr stmt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_reset(IntPtr stmt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_clear_bindings(IntPtr stmt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_int64(IntPtr stmt, int index, long value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_double(IntPtr stmt, int index, double value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] value, int nByte, IntPtr destructor);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern long sqlite3_column_int64(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern double sqlite3_column_double(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_bytes(IntPtr stmt, int column);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sqlite3_column_type(IntPtr stmt, int column);

        // --- Native library loading -------------------------------------------------------------
        //
        // The native library ships next to ValheimRadar.dll in the BepInEx plugin folder, which isn't on
        // the OS library search path. Loading it once by full path makes every later DllImport("e_sqlite3")
        // resolve to that already-loaded module (Windows matches loaded modules by base name, glibc by
        // SONAME - libe_sqlite3.so's SONAME is its own file name). When the file isn't there (e.g. the
        // unit tests, where .NET resolves runtimes/<rid>/native itself) the normal lookup is used.

        private static bool preloadAttempted;

        internal static void Preload(string directory)
        {
            if (preloadAttempted) return;
            preloadAttempted = true;
            if (string.IsNullOrEmpty(directory)) return;

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                string path = Path.Combine(directory, "e_sqlite3.dll");
                if (File.Exists(path)) LoadLibraryW(path);
            }
            else
            {
                string path = Path.Combine(directory, "libe_sqlite3.so");
                if (!File.Exists(path)) return;

                try
                {
                    dlopen2(path, RTLD_NOW | RTLD_GLOBAL);
                }
                catch (DllNotFoundException)
                {
                    dlopen(path, RTLD_NOW | RTLD_GLOBAL);
                }
            }
        }

        private const int RTLD_NOW = 2;
        private const int RTLD_GLOBAL = 0x100;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        [DllImport("libdl.so.2", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen2(string path, int flags);

        [DllImport("libdl", EntryPoint = "dlopen")]
        private static extern IntPtr dlopen(string path, int flags);
    }
}
