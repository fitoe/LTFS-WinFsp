param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$ToolchainDirectory,
    [Parameter(Mandatory = $true)][string]$FuseHeaderDirectory,
    [string]$OutputDirectory,
    [string[]]$Only,
    [switch]$WinFspBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$toolchain = (Resolve-Path -LiteralPath $ToolchainDirectory).Path
$headers = (Resolve-Path -LiteralPath $FuseHeaderDirectory).Path
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'obj' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$gcc = Join-Path $toolchain 'bin\gcc.exe'
foreach ($file in @($gcc, "$headers\fuse.h", "$source\src\libltfs\Makefile.am_Windows")) {
    if (!(Test-Path -LiteralPath $file)) { throw "Missing audit input: $file" }
}
# Use the actual upstream Windows source list, including core write operations.
$makefile = Get-Content -Raw -LiteralPath "$source\src\libltfs\Makefile.am_Windows"
$list = [regex]::Match($makefile, '(?ms)^libltfs_la_SOURCES\s*=\s*(.*?)(?=^\s*#)')
if (!$list.Success) { throw 'Cannot locate upstream core source list.' }
$files = @([regex]::Matches($list.Groups[1].Value, '[a-zA-Z0-9_/]+\.c') | ForEach-Object { 'libltfs/' + $_.Value })
$files += @('ltfs_fuse.c', 'main.c', 'iosched/fcfs.c', 'iosched/unified.c', 'iosched/cache_manager.c',
    'tape_drivers/windows/ltotape/ltotape.c', 'tape_drivers/windows/ltotape/ltotape_diag.c',
    'tape_drivers/windows/ltotape/ltotape_platform.c')
if ($Only) {
    foreach ($item in $Only) { if ($item -notin $files) { throw "Unknown audit source: $item" } }
    $files = @($files | Where-Object { $_ -in $Only })
}
$flags = @('-c', '-std=gnu11', '-fmax-errors=8', '-D_GNU_SOURCE', '-Dmingw_PLATFORM=1',
    '-DHPE_mingw_BUILD=1', '-DHPE_BUILD=1', '-DLTFS_MINGW_W64=1', '-D_FILE_OFFSET_BITS=64',
    "-I$source/src", "-I$source/src/libltfs", "-I$PSScriptRoot/audit-config", "-I$headers",
    "-I$toolchain/include/libxml2")
if ($WinFspBuild) { $flags += '-DLTFS_WINFSP_BUILD=1' }
$savedPath = $env:PATH
$results = @()
try {
    $env:PATH = "$toolchain\bin;$savedPath"
    $version = (& $gcc --version | Select-Object -First 1)
    foreach ($file in $files) {
        $name = $file.Replace('/', '_')
        $fileFlags = @($flags)
        # This unit normally gets LTFS_CONFIG_FILE from autotools CPPFLAGS.
        if ($file -eq 'libltfs/config_file.c') {
            $fileFlags += @('-include', "$PSScriptRoot/audit-config/config.h")
        }
        # An audit is successful only when the compiler succeeds in this run,
        # never based on a possibly stale object file.
        $ErrorActionPreference = 'Continue' # Windows PowerShell wraps native stderr as errors.
        try {
            $diagnostics = & $gcc @fileFlags "$source/src/$file" -o "$output/$name.o" 2>&1
            $code = $LASTEXITCODE
        } finally { $ErrorActionPreference = 'Stop' }
        $diagnostics | ForEach-Object { $_.ToString() } | Out-File -Encoding utf8 -LiteralPath "$output/$name.log"
        $results += [pscustomobject]@{ source = $file; exitCode = $code; log = "$name.log"; flags = $fileFlags }
        Write-Host ("{0}: {1}" -f $(if ($code -eq 0) { 'PASS' } else { 'FAIL' }), $file)
    }
    [pscustomobject]@{
        compiler = $version; sourceDirectory = $source; flags = $flags
        kind = 'compile-only; no linking, execution or tape access'; results = $results
    } | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 -LiteralPath "$output/results.json"
} finally { $env:PATH = $savedPath }
$failures = @($results | Where-Object exitCode -ne 0).Count
Write-Host "$($results.Count - $failures)/$($results.Count) translation units compiled. Reports: $output"
if ($failures) { exit 1 }
