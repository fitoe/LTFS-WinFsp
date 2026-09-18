param(
    [Parameter(Mandatory = $true)][string]$HeaderDirectory,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (!$OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot 'obj' }
foreach ($name in @('fuse.h', 'fuse_common.h', 'fuse_opt.h', 'winfsp_fuse.h')) {
    if (!(Test-Path -LiteralPath (Join-Path $HeaderDirectory $name))) {
        throw "Missing WinFsp SDK header: $name"
    }
}
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'Visual Studio C++ Build Tools are required.' }
Import-Module (Join-Path $installation 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$object = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'fuse26-probe.obj'
& cl.exe /nologo /c /TC /std:c11 /W4 /WX "/I$HeaderDirectory" "/Fo$object" (Join-Path $PSScriptRoot 'fuse26-probe.c')
if ($LASTEXITCODE -ne 0) { throw 'FUSE interface compilation failed.' }
Write-Host 'PASS: x64 FUSE callback declarations compile. No link, mount, or LTFS engine test performed.'
