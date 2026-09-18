using System.Diagnostics;

namespace LTFS.WinFsp.Desktop;

internal sealed class MountSession : IDisposable
{
    private readonly Process process = new();
    private Task<string>? errors;
    private bool started;
    public bool HasExited => !started || process.HasExited;

    public async Task StartAsync(string device, string drive, CancellationToken token)
    {
        process.StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("--worker");
        process.StartInfo.ArgumentList.Add(device == "simulate" ? "simulate" : "tape");
        if (device != "simulate") process.StartInfo.ArgumentList.Add(device);
        process.StartInfo.ArgumentList.Add(drive);
        token.ThrowIfCancellationRequested();
        started = process.Start();
        errors = process.StandardError.ReadToEndAsync();
        try
        {
            string? line = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromMinutes(5), token);
            if (line == null || !line.StartsWith("Mounted ", StringComparison.Ordinal))
            {
                string error = await errors.WaitAsync(TimeSpan.FromSeconds(2), token);
                throw new IOException(error.Length == 0 ? "挂载进程意外退出。" : error);
            }
            token.ThrowIfCancellationRequested();
        }
        catch { await StopAsync(); throw; }
    }

    public async Task StopAsync()
    {
        if (!started || process.HasExited) return;
        try { await process.StandardInput.WriteLineAsync(); await process.StandardInput.FlushAsync(); }
        catch (IOException) { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) when (process.HasExited) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { throw new IOException("设备驱动仍未释放工作进程。请勿重复挂载；可能需要恢复设备连接或重启 Windows。"); }
        }
    }
    public void Dispose() => process.Dispose();
}
