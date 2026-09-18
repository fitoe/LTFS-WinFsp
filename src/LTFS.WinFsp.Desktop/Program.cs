using Fsp;
using LTFS.WinFsp.Windows;

namespace LTFS.WinFsp.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--worker")
        {
            Environment.ExitCode = MountWorker.RunAsync(args[1..]).GetAwaiter().GetResult();
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MountWindow());
    }
}

internal sealed class MountWindow : Form
{
    private readonly ComboBox device = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox drive = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button mount = new() { Name = "mountButton", Text = "挂载（只读）", AutoSize = true };
    private readonly Button unmount = new() { Name = "unmountButton", Text = "卸载", AutoSize = true, Enabled = false };
    private readonly Label status = new() { Text = "未挂载 · 当前仅支持模拟磁带", AutoSize = true, Dock = DockStyle.Fill };
    private MountSession? host;
    private CancellationTokenSource? mountCancellation;
    private bool closeRequested;
    private bool busy;
    private bool closing;
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 1000 };

    public MountWindow()
    {
        Text = "LTFS-WinFsp";
        ClientSize = new Size(560, 240);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "磁带机", AutoSize = true }, 0, 0);
        layout.Controls.Add(device, 1, 0);
        layout.Controls.Add(new Label { Text = "盘符", AutoSize = true }, 0, 1);
        layout.Controls.Add(drive, 1, 1);
        device.Items.Add("模拟磁带（测试数据，不连接真实设备）");
        device.SelectedIndex = 0;
        RefreshDevices();
        device.DropDown += (_, _) => RefreshDevices();
        device.SelectedIndexChanged += (_, _) =>
        {
            if (host == null && !busy)
                status.Text = device.SelectedIndex == 0 ? "未挂载 · 模拟磁带" : "真实磁带 · 实验性只读挂载，尚待真机验证";
            UpdateControls();
        };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([mount, unmount]);
        layout.Controls.Add(buttons, 1, 2);
        layout.Controls.Add(status, 0, 3);
        layout.SetColumnSpan(status, 2);
        var credit = new LinkLabel { Text = "WinFsp - Windows File System Proxy, Copyright (C) Bill Zissimopoulos", AutoSize = true };
        credit.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/winfsp/winfsp") { UseShellExecute = true });
        layout.Controls.Add(credit, 0, 4);
        layout.SetColumnSpan(credit, 2);
        Controls.Add(layout);
        RefreshDrives();
        drive.DropDown += (_, _) => RefreshDrives();
        drive.SelectedIndexChanged += (_, _) => UpdateControls();
        mount.Click += async (_, _) => await MountAsync();
        unmount.Click += async (_, _) => { if (mountCancellation != null) mountCancellation.Cancel(); else await UnmountAsync(); };
        FormClosing += async (_, e) =>
        {
            if (closing) return;
            if (busy) { e.Cancel = true; closeRequested = true; mountCancellation?.Cancel(); return; }
            if (host == null) return;
            e.Cancel = true;
            await UnmountAsync();
            if (host == null) { closing = true; Close(); }
        };
        monitor.Tick += (_, _) =>
        {
            if (!busy && host != null && host.HasExited)
            {
                host.Dispose(); host = null;
                status.Text = "工作进程已退出，挂载已失效。";
                RefreshDrives();
            }
        };
        monitor.Start();
        FormClosed += (_, _) => monitor.Dispose();
    }

    private void RefreshDrives()
    {
        var previous = drive.SelectedItem as string ?? "L:";
        var used = DriveInfo.GetDrives().Select(d => d.Name[..2]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        drive.Items.Clear();
        for (char letter = 'D'; letter <= 'Z'; letter++)
            if (!used.Contains($"{letter}:")) drive.Items.Add($"{letter}:");
        if (drive.Items.Contains(previous)) drive.SelectedItem = previous;
        else if (drive.Items.Count > 0) drive.SelectedIndex = 0;
        UpdateControls();
    }

    private void RefreshDevices()
    {
        if (busy || host != null) return;
        string? selected = device.SelectedItem as string;
        try
        {
            var names = WindowsTape.EnumerateDevices();
            while (device.Items.Count > 1) device.Items.RemoveAt(1);
            foreach (var name in names) device.Items.Add(name);
            device.SelectedItem = selected;
            if (device.SelectedIndex < 0) device.SelectedIndex = 0;
        }
        catch (Exception ex) { status.Text = "设备枚举失败：" + ex.Message; }
    }

    private void UpdateControls()
    {
        device.Enabled = drive.Enabled = !busy && host == null;
        mount.Enabled = !busy && host == null && drive.SelectedItem != null && device.SelectedIndex >= 0;
        unmount.Enabled = host != null && (!busy || mountCancellation != null);
        unmount.Text = mountCancellation != null ? "取消" : "卸载";
    }

    private async Task MountAsync()
    {
        if (busy || host != null || device.SelectedIndex < 0 || drive.SelectedItem is not string letter) return;
        string selected = device.SelectedIndex == 0 ? "simulate" : (string)device.SelectedItem!;
        busy = true;
        mountCancellation = new CancellationTokenSource();
        host = new MountSession();
        UpdateControls(); status.Text = "正在读取标签和索引并挂载…";
        try
        {
            await host.StartAsync(selected, letter, mountCancellation.Token);
            status.Text = $"已挂载 {letter} · 只读 · {selected}";
        }
        catch (Exception ex)
        {
            if (host.HasExited) { host.Dispose(); host = null; }
            status.Text = ex is OperationCanceledException ? "已取消" : "挂载失败";
            if (ex is not OperationCanceledException) MessageBox.Show(this, ex.Message, "挂载失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            mountCancellation.Dispose(); mountCancellation = null;
            busy = false; UpdateControls();
            if (closeRequested && host == null) { closing = true; Close(); }
        }
    }

    private async Task UnmountAsync()
    {
        if (busy || host == null) return;
        busy = true; UpdateControls(); status.Text = "正在卸载…";
        try
        {
            await host.StopAsync();
            host.Dispose(); host = null;
            status.Text = "已卸载";
        }
        catch (Exception ex) { status.Text = "卸载失败，可重试"; MessageBox.Show(this, ex.Message, "卸载失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { busy = false; RefreshDrives(); if (closeRequested && host == null) { closing = true; Close(); } }
    }
}
