#nullable enable
using Fsp;
using LTFS.WinFsp.Windows;

public static class MountWorker
{
    public static async Task<int> RunAsync(string[] args)
    {
        bool simulated = args.Length == 2 && args[0] == "simulate";
        bool physical = args.Length == 3 && args[0] == "tape";
        if ((!simulated && !physical) || !System.Text.RegularExpressions.Regex.IsMatch(args[^1], "^[D-Zd-z]:$")) return 2;
        string drive = args[^1];
        Console.Error.WriteLine("WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos. https://github.com/winfsp/winfsp");
        using var stop = new CancellationTokenSource();
        // EOF (including parent exit) also asks the child to stop.
        _ = Task.Run(async () => { await Console.In.ReadLineAsync(); stop.Cancel(); });
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        WindowsTape? tape = null;
        SimulationFileSystem? fs = null;
        try
        {
            if (DriveInfo.GetDrives().Any(d => d.Name.StartsWith(drive, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Drive letter is already in use.");
            if (simulated) fs = new SimulationFileSystem();
            else
            {
                tape = new WindowsTape(args[1]);
                var loaded = await TapeVolumeLoader.LoadAsync(tape, stop.Token);
                fs = new SimulationFileSystem(tape, loaded.Volume, loaded.BlockSize);
            }
            stop.Token.ThrowIfCancellationRequested();
            using var host = new FileSystemHost(fs);
            int status = host.Mount(drive, null, true, 0);
            if (status < 0) throw new IOException($"Mount failed: 0x{status:X8}");
            Console.WriteLine($"Mounted {(simulated ? "simulated" : "physical")} read-only volume at {drive}.");
            Console.Out.Flush();
            try { await Task.Delay(Timeout.Infinite, stop.Token); }
            catch (OperationCanceledException) { }
            host.Unmount();
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally
        {
            if (fs != null) fs.Dispose();
            else if (tape != null) await tape.DisposeAsync();
        }
    }
}
