param([string]$WinFspAssembly, [string]$Output)
$ErrorActionPreference = 'Stop'
if (!$WinFspAssembly) {
    $base = Join-Path ${env:ProgramFiles(x86)} 'WinFsp'
    $WinFspAssembly = Get-ChildItem -LiteralPath $base -Filter winfsp-msil.dll -Recurse |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$WinFspAssembly -or !(Test-Path -LiteralPath $WinFspAssembly)) { throw 'Install the official WinFsp runtime first.' }
Push-Location $PSScriptRoot
try {
    dotnet test LTFS.WinFsp.sln -c Release
    if ($LASTEXITCODE) { throw 'Core tests failed.' }
    dotnet test tests/LTFS.WinFsp.Windows.Tests -c Release
    if ($LASTEXITCODE) { throw 'Windows tests failed.' }
    $arguments = @('publish', 'src/LTFS.WinFsp.Desktop', '-c', 'Release', "-p:WinFspAssembly=$WinFspAssembly")
    if ($Output) { $arguments += @('-o', $Output) }
    & dotnet @arguments
    if ($LASTEXITCODE) { throw 'Desktop build failed.' }
}
finally { Pop-Location }
