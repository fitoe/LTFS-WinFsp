using System.Diagnostics;
using System.Security.Cryptography;

namespace HpeReadProbe;

public sealed record ReadResult(
    int RequestBytes, long LimitBytes, long BytesRead, long ReadCalls,
    long ShortReads, bool ObservedEof, double Seconds,
    double ReadCallSeconds, double MaxReadMilliseconds, string Sha256)
{
    public double MiBPerSecond => Seconds > 0 ? BytesRead / 1048576d / Seconds : 0;
}

public static class ReadProbe
{
    // This measures application calls, NOT SCSI commands or physical tape motion.
    public static ReadResult Measure(Stream source, int requestBytes, long limitBytes,
        CancellationToken cancellation = default)
    {
        if (!source.CanRead) throw new ArgumentException("Source must be readable.");
        if (requestBytes < 1 || requestBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(requestBytes));
        if (limitBytes < 1) throw new ArgumentOutOfRangeException(nameof(limitBytes));
        var buffer = new byte[requestBytes];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytes = 0, calls = 0, shortReads = 0, readTicks = 0, maxTicks = 0;
        bool eof = false;
        var start = Stopwatch.GetTimestamp();
        while (bytes < limitBytes)
        {
            cancellation.ThrowIfCancellationRequested();
            int wanted = (int)Math.Min(requestBytes, limitBytes - bytes);
            long before = Stopwatch.GetTimestamp();
            int actual = source.Read(buffer, 0, wanted);
            long elapsed = Stopwatch.GetTimestamp() - before;
            calls++;
            readTicks += elapsed;
            maxTicks = Math.Max(maxTicks, elapsed);
            if (actual < 0 || actual > wanted) throw new IOException("Invalid stream read result.");
            if (actual == 0) { eof = true; break; }
            if (actual < wanted) shortReads++;
            hash.AppendData(buffer, 0, actual);
            bytes += actual;
        }
        cancellation.ThrowIfCancellationRequested();
        string digest = Convert.ToHexString(hash.GetHashAndReset());
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        return new(requestBytes, limitBytes, bytes, calls, shortReads, eof, seconds,
            readTicks / (double)Stopwatch.Frequency,
            maxTicks * 1000d / Stopwatch.Frequency, digest);
    }

    public static string ValidatePath(string path)
    {
        // File-only tool. Never accept Win32 raw devices or extended device namespaces.
        string full = Path.GetFullPath(path).Replace('/', '\\');
        if (full.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            full.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            full.StartsWith(@"\??\", StringComparison.Ordinal))
            throw new ArgumentException("Use an ordinary mounted file path, not a device path.");
        if (!File.Exists(full)) throw new FileNotFoundException("Source file not found.", full);
        return full;
    }
}
