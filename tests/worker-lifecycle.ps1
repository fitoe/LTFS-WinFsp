param([Parameter(Mandatory=$true)][string]$Executable, [string]$Drive = 'L:')
$ErrorActionPreference = 'Stop'
if ($Drive -notmatch '^[D-Zd-z]:$' -or (Test-Path "$Drive\")) { throw 'Requires a free drive letter.' }
function Start-Worker {
    $p = [Diagnostics.Process]::new()
    $p.StartInfo.FileName = $Executable
    $p.StartInfo.Arguments = '--worker simulate ' + $Drive
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.CreateNoWindow = $true
    $p.StartInfo.RedirectStandardInput = $true
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    $null = $p.Start()
    return $p
}
function Assert-Ready($p) {
    $ready = $p.StandardOutput.ReadLineAsync()
    if (!$ready.Wait(10000) -or $ready.Result -notmatch '^Mounted ') { throw 'Worker failed to mount.' }
}
function Assert-Unmounted {
    $end = [DateTime]::UtcNow.AddSeconds(10)
    while ((Test-Path "$Drive\") -and [DateTime]::UtcNow -lt $end) { Start-Sleep -Milliseconds 100 }
    if (Test-Path "$Drive\") { throw 'Drive was not released.' }
}
$first = $null; $second = $null
try {
    # Early cancellation/parent EOF during startup must not leave a late mount.
    $first = Start-Worker
    $first.StandardInput.Close()
    if (!$first.WaitForExit(10000)) { throw 'Early cancellation hung.' }
    Assert-Unmounted
    $first.Dispose(); $first = $null

    # Occupied-drive failure must leave the original worker and mount intact.
    $first = Start-Worker
    Assert-Ready $first
    $second = Start-Worker
    if (!$second.WaitForExit(10000) -or $second.ExitCode -eq 0) { throw 'Occupied drive was not rejected.' }
    if (!(Test-Path "$Drive\samples\sample.bin") -or $first.HasExited) { throw 'Original mount was damaged.' }
    $second.Dispose(); $second = $null

    # Parent losing stdin closes the worker even after mount succeeds.
    $first.StandardInput.Close()
    if (!$first.WaitForExit(10000)) { throw 'Parent EOF did not stop worker.' }
    Assert-Unmounted
    $first.Dispose(); $first = $null

    # Simulate a user-mode worker crash; this does not simulate a stuck kernel driver.
    $first = Start-Worker
    Assert-Ready $first
    $first.Kill()
    if (!$first.WaitForExit(10000)) { throw 'Worker did not terminate.' }
    Assert-Unmounted
    'PASS: early cancellation, occupied-drive rejection, parent EOF, worker crash cleanup.'
}
finally {
    foreach ($p in @($first, $second)) {
        if ($null -ne $p) {
            if (!$p.HasExited) { $p.Kill(); $null = $p.WaitForExit(10000) }
            $p.Dispose()
        }
    }
}
