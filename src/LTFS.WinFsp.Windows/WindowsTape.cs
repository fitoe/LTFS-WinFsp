using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using LTFS.WinFsp.Core;

namespace LTFS.WinFsp.Windows;

/// <summary>
/// Read-only operations over the Windows tape API. No write/format/erase exports.
/// Synchronous driver calls cannot be interrupted by a CancellationToken: the host
/// must isolate this backend before exposing physical mounts in the UI.
/// Partition numbers here are native Windows tape numbers, not LTFS label IDs.
/// </summary>
public sealed class WindowsTape : IReadOnlyTape
{
    private readonly SafeFileHandle handle;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool disposed;
    public string DeviceId { get; }

    public static IReadOnlyList<string> EnumerateDevices()
    {
        // Query namespace only: no device is opened or moved during enumeration.
        for (int capacity = 4096; capacity <= 1024 * 1024; capacity *= 2)
        {
            var buffer = new char[capacity];
            uint result = Native.QueryDosDevice(null, buffer, buffer.Length);
            if (result != 0)
                return new string(buffer, 0, (int)result).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, "^Tape[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
            int error = Marshal.GetLastWin32Error();
            if (error != 122) throw Error((uint)error, "Enumerate tape devices");
        }
        throw new IOException("Device namespace exceeded enumeration limit.");
    }

    public WindowsTape(string deviceId)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(deviceId, "^Tape[0-9]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Expected Tape0, Tape1, etc.", nameof(deviceId));
        DeviceId = deviceId;
        // Windows tape control APIs require GENERIC_READ | GENERIC_WRITE. This
        // handle access is not write protection. Use a physically write-protected
        // cartridge for initial hardware validation; this class exports no writes.
        handle = Native.CreateFile(@"\\.\" + deviceId, 0xC0000000, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            uint error = (uint)Marshal.GetLastWin32Error();
            handle.Dispose();
            throw Error(error, "Open tape exclusively");
        }
    }

    private async ValueTask<T> ExecuteAsync<T>(Func<T> operation, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            token.ThrowIfCancellationRequested();
            // Await the actual completion; never release the gate while the driver
            // still owns a request or its read buffer.
            T result = await Task.Run(operation).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return result;
        }
        finally { gate.Release(); }
    }

    public ValueTask<TapePosition> GetPositionAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() =>
        {
            uint status = Native.GetTapePosition(handle, 1, out uint partition, out uint low, out uint high);
            Check(status, "Read tape position");
            ulong block = ((ulong)high << 32) | low;
            if (partition > byte.MaxValue || block > long.MaxValue) throw new IOException("Tape position exceeds supported range.");
            return new TapePosition((byte)partition, (long)block);
        }, cancellationToken);

    public async ValueTask LocateAsync(TapePosition position, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position.Block);
        await ExecuteAsync(() =>
        {
            Check(Native.SetTapePosition(handle, 2, position.Partition, (uint)position.Block, (uint)((ulong)position.Block >> 32), false), "Locate logical tape block");
            return true;
        }, cancellationToken);
    }

    public async ValueTask<int> ReadBlockAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
    {
        if (destination.IsEmpty || destination.Length > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(destination));
        byte[] buffer = new byte[destination.Length];
        int count = await ExecuteAsync(() =>
        {
            if (!Native.ReadFile(handle, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero))
                throw Error((uint)Marshal.GetLastWin32Error(), "Read tape block");
            if (read > buffer.Length) throw new IOException("Driver returned an invalid byte count.");
            return (int)read;
        }, cancellationToken);
        buffer.AsMemory(0, count).CopyTo(destination);
        return count;
    }

    public ValueTask<bool> CheckReadyAsync(CancellationToken token = default) => ExecuteAsync(() =>
    { Check(Native.GetTapeStatus(handle), "Check tape readiness"); return true; }, token);

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { if (!disposed) { disposed = true; handle.Dispose(); } }
        finally { gate.Release(); }
    }
    private static void Check(uint status, string operation) { if (status != 0) throw Error(status, operation); }
    private static IOException Error(uint status, string operation) => new TapeDeviceException(status, operation);

    private static class Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern uint QueryDosDevice(string? name, [Out] char[] target, int capacity);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReadFile(SafeFileHandle file, [Out] byte[] buffer, uint size, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll")]
        internal static extern uint GetTapePosition(SafeFileHandle tape, uint type, out uint partition, out uint low, out uint high);
        [DllImport("kernel32.dll")]
        internal static extern uint SetTapePosition(SafeFileHandle tape, uint method, uint partition, uint low, uint high, [MarshalAs(UnmanagedType.Bool)] bool immediate);
        [DllImport("kernel32.dll")]
        internal static extern uint GetTapeStatus(SafeFileHandle tape);
    }
}

public sealed class TapeDeviceException : IOException
{
    public uint NativeError { get; }
    public bool IsFilemark => NativeError == 1101;
    public bool IsEndOfData => NativeError is 1104 or 38;
    public TapeDeviceException(uint error, string operation)
        : base($"{operation}: {new Win32Exception((int)error).Message} (Win32 {error})") => NativeError = error;
}
