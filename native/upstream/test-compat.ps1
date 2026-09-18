param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$ToolchainDirectory,
    [Parameter(Mandatory = $true)][string]$FuseHeaderDirectory
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$toolchain = (Resolve-Path -LiteralPath $ToolchainDirectory).Path
$headers = (Resolve-Path -LiteralPath $FuseHeaderDirectory).Path
$output = Join-Path $PSScriptRoot 'obj'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$savedPath = $env:PATH
try {
    $env:PATH = "$toolchain\bin;$savedPath"
    & "$toolchain\bin\gcc.exe" -std=gnu11 -Werror=incompatible-pointer-types `
        -D_GNU_SOURCE -Dmingw_PLATFORM=1 -DHPE_mingw_BUILD=1 -DHPE_BUILD=1 `
        -DLTFS_MINGW_W64=1 -DLTFS_WINFSP_BUILD=1 -D_FILE_OFFSET_BITS=64 `
        "-I$source/src" "-I$PSScriptRoot/audit-config" "-I$headers" `
        "$PSScriptRoot/compat-smoke.c" -o "$output/compat-smoke.exe" -lpthread
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility smoke test did not compile.' }
    & "$output/compat-smoke.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility smoke test failed.' }
} finally { $env:PATH = $savedPath }
