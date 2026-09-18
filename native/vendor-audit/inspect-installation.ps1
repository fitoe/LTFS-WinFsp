param(
    [Parameter(Mandatory = $true)][string]$ObjdumpPath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$InstallDirectory = 'C:\Program Files\LTFS',
    [string]$ConfigPath = 'C:\ProgramData\Hewlett-Packard\LTFS\ltfs.conf'
)
# Static inspection only: never load vendor DLLs, launch vendor programs,
# change services/registry/configuration, or open a tape device.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$objdump = (Resolve-Path -LiteralPath $ObjdumpPath).Path
$installation = (Resolve-Path -LiteralPath $InstallDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$modules = @()
foreach ($name in @('libltfs.dll', 'libiosched-unified.dll', 'libiosched-fcfs.dll',
    'libdriver-ltotape-win.dll', 'FUSE4Win.dll', 'UMFSDKDLL.DLL',
    'pthreadGC2-w64.dll', 'icuuc50.dll', 'LTFSConfigurator.exe')) {
    $path = Join-Path $installation $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing component: $path" }
    $item = Get-Item -LiteralPath $path
    $pe = @(& $objdump -p $path)
    if ($LASTEXITCODE -ne 0) { throw "Static PE inspection failed: $name" }
    $pe | Out-File -LiteralPath (Join-Path $output "$name.pe.txt") -Encoding utf8
    $imports = @($pe | ForEach-Object {
        if ($_ -match 'DLL Name:\s*(\S+)') { $Matches[1] }
    } | Sort-Object -Unique)
    $modules += [pscustomobject]@{
        name = $name; fileVersion = $item.VersionInfo.FileVersion
        bytes = $item.Length; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        imports = $imports; format = @($pe | Where-Object { $_ -match 'file format' })
    }
}
$config = @()
if (Test-Path -LiteralPath $ConfigPath -PathType Leaf) {
    $config = @(Get-Content -LiteralPath $ConfigPath | Where-Object { $_ -match '^\s*(plugin|default)\s+' })
}
$service = Get-Service -Name FUSE4WinSvc -ErrorAction SilentlyContinue
$report = [pscustomobject]@{
    inspectedAt = (Get-Date).ToString('o'); method = 'static file inspection; no vendor code or device access'
    installDirectory = $installation
    serviceStatus = $(if ($service) { $service.Status.ToString() } else { 'NotFound' })
    activeConfigLines = $config; modules = $modules
    compatibility = 'Not established. Exported names alone do not establish structure layout or runtime ABI compatibility.'
}
$report | ConvertTo-Json -Depth 6 | Out-File -LiteralPath (Join-Path $output 'installation.json') -Encoding utf8
Write-Host "Inspected $($modules.Count) components. No installation changes. Report: $output"
