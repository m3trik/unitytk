using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The exclusive lock on a session's <c>instance.lock</c>, held for the
    /// editor's life: held means this instance is up, which is how pythontk's
    /// caller tells (a heartbeat lags under a long call; a pid may name another
    /// process by now). Byte 0, as Python's <c>msvcrt.locking</c> / <c>fcntl.flock</c>
    /// lock it. The handle is the process's, so it outlives a domain reload; its
    /// number rides in <see cref="SessionState"/>, so the reloaded domain knows it
    /// holds the lock and never takes it twice.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>FileStream.Lock</c> takes a POSIX record lock (<c>fcntl</c>) off
    /// Windows, which Python's <c>fcntl.flock</c> never sees, and its handle closes when
    /// the managed stream is collected (a domain reload); so the lock is the OS's own,
    /// taken as Python takes it.
    /// </remarks>
    internal static class InstanceLock
    {
        const string HandleKey = "M3trik.Unitytk.Kernel.InstanceLock";

        /// <summary>Whether this process holds the lock (taken before a reload counts).</summary>
        public static bool Held => SessionState.GetString(HandleKey, "") != "";

        /// <summary>Take the lock on <paramref name="path"/> without waiting; false when another process holds it.</summary>
        public static bool TryTake(string path)
        {
            if (Held) return true;
            long handle = IsWindows() ? WindowsTake(path) : PosixTake(path);
            if (handle == -1) return false;
            SessionState.SetString(HandleKey, handle.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>Let the lock go (the process ending lets it go too).</summary>
        public static void Release()
        {
            string stored = SessionState.GetString(HandleKey, "");
            SessionState.EraseString(HandleKey);
            if (stored == "") return;
            long handle = long.Parse(stored, CultureInfo.InvariantCulture);
            if (IsWindows())
            {
                UnlockFile(new IntPtr(handle), 0, 0, 1, 0);
                CloseHandle(new IntPtr(handle));
            }
            else
            {
                close((int)handle);
            }
        }

        static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // -- Windows --------------------------------------------------------------
        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
        const uint ShareAll = 0x1 | 0x2 | 0x4, OpenAlways = 4, Normal = 0x80;

        static long WindowsTake(string path)
        {
            IntPtr h = CreateFileW(path, GenericRead | GenericWrite, ShareAll, IntPtr.Zero, OpenAlways, Normal, IntPtr.Zero);
            if (h == new IntPtr(-1)) return -1;
            if (LockFile(h, 0, 0, 1, 0)) return h.ToInt64();
            CloseHandle(h);
            return -1;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition,
                                         uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool LockFile(IntPtr file, uint offsetLow, uint offsetHigh, uint lengthLow, uint lengthHigh);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool UnlockFile(IntPtr file, uint offsetLow, uint offsetHigh, uint lengthLow, uint lengthHigh);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);

        // -- macOS and Linux: flock, as Python's fcntl.flock --------------------------
        const int LockExclusive = 2, LockNoWait = 4, ReadWrite = 2;

        static long PosixTake(string path)
        {
            // The file is made through .NET, so open() never takes its variadic mode
            // argument (passed otherwise on some ABIs); close-on-exec keeps a child the
            // editor starts (a compiler) from holding the lock past it.
            using (new System.IO.FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.ReadWrite,
                                            System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete)) { }
            int closeOnExec = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? 0x1000000 : 0x80000;
            int fd = open(path, ReadWrite | closeOnExec);
            if (fd < 0) return -1;
            if (flock(fd, LockExclusive | LockNoWait) == 0) return fd;
            close(fd);
            return -1;
        }

        [DllImport("libc", SetLastError = true)]
        static extern int open(string path, int flags);

        [DllImport("libc", SetLastError = true)]
        static extern int flock(int fd, int operation);

        [DllImport("libc", SetLastError = true)]
        static extern int close(int fd);
    }
}
