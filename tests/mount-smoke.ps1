param([string]$Drive = 'L:')
$ErrorActionPreference = 'Stop'
if ($Drive -notmatch '^[D-Zd-z]:$') { throw 'Use a drive letter D: through Z:.' }
if (Test-Path "$Drive\") { throw 'Drive already in use.' }
$projectRoot = Split-Path $PSScriptRoot
$dll = Join-Path $projectRoot 'src\LTFS.WinFsp.Mount\bin\Release\net8.0-windows\LTFS.WinFsp.Mount.dll'
for ($round = 0; $round -lt 2; $round++) {
    $p = [Diagnostics.Process]::new()
    $p.StartInfo.FileName = 'dotnet'
    $p.StartInfo.Arguments = '"' + $dll + '" simulate ' + $Drive
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.CreateNoWindow = $true
    $p.StartInfo.RedirectStandardInput = $true
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    $null = $p.Start()
    try {
        $ready = $p.StandardOutput.ReadLineAsync()
        if (!$ready.Wait(10000) -or $ready.Result -notmatch '^Mounted') { throw 'Mount failed or timed out.' }
        $names = @(Get-ChildItem "$Drive\samples" | Select-Object -ExpandProperty Name)
        if ($names.Count -ne 2) { throw "Unexpected directory listing: $($names.Count): $names" }
        foreach ($sample in @(@('sample.bin', 16777216, 0), @('cross-block.bin', 100000, 65000))) {
            $data = [IO.File]::ReadAllBytes("$Drive\samples\$($sample[0])")
            if ($data.Length -ne $sample[1]) { throw 'Incorrect length.' }
            for ($i = 0; $i -lt $data.Length; $i++) {
                $physical = $i + $sample[2]
                $expected = (([math]::Floor($physical / 65536) * 17) + ($physical % 65536)) % 251
                if ($data[$i] -ne $expected) { throw "Data mismatch at $i" }
            }
        }
        $denied = $false
        try { [IO.File]::WriteAllText("$Drive\rejected.txt", 'must fail') }
        catch [System.UnauthorizedAccessException] { $denied = $true }
        catch [System.IO.IOException] { $denied = $true }
        if (!$denied) { throw 'Write was not rejected.' }
    }
    finally {
        if (!$p.HasExited) {
            $p.StandardInput.WriteLine()
            if (!$p.WaitForExit(10000)) { $p.Kill(); throw 'Unmount timed out.' }
        }
        $errors = $p.StandardError.ReadToEnd()
        if ($errors) { Write-Warning $errors }
        if (Test-Path "$Drive\") { throw 'Mount remained after shutdown.' }
        $p.Dispose()
    }
    Write-Output "Round $round passed: listing, full data verification, write protection, unmount."
}
