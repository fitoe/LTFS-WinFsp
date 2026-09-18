using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using HpeReadProbe;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine("HpeReadProbe <mounted-file> <request-KiB: 1..65536> <limit-MiB: positive integer>");
    Console.WriteLine("Read-only application-level probe. JSON on stdout; errors on stderr. No raw tape access.");
    Console.WriteLine("Uses normal Windows caching. Hash/throughput cover only the consumed prefix, not necessarily the whole file.");
    return 0;
}
if (args.Length != 3 || !int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int kib) ||
    kib < 1 || kib > 65536 || !long.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out long mib) ||
    mib < 1 || mib > long.MaxValue / 1048576)
{
    Console.Error.WriteLine("Usage: HpeReadProbe <mounted-file> <request-KiB: 1..65536> <limit-MiB>");
    return 2;
}
using var cancelled = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelled.Cancel(); };
try
{
    string path = ReadProbe.ValidatePath(args[0]);
    long opened = Stopwatch.GetTimestamp();
    using var input = new FileStream(path, new FileStreamOptions
    {
        Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read,
        BufferSize = 1, Options = FileOptions.SequentialScan
    });
    double openSeconds = Stopwatch.GetElapsedTime(opened).TotalSeconds;
    var result = ReadProbe.Measure(input, checked(kib * 1024), checked(mib * 1048576), cancelled.Token);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Kind = "application-level sequential read; not a physical tape benchmark",
        CachePolicy = "normal OS caching; SequentialScan hint; FileStream buffer disabled",
        HashScope = "consumed file prefix from offset zero",
        OpenSeconds = openSeconds, Result = result
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; no successful result emitted."); return 130; }
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
