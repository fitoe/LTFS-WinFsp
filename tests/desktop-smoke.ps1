param([Parameter(Mandatory=$true)][string]$Executable)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if (Test-Path L:\) { throw 'UI test requires a free L: drive.' }
$process = Start-Process -FilePath $Executable -PassThru
function Wait-Until([scriptblock]$Condition, [string]$Message) {
    $end = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $end) {
        if (& $Condition) { return }
        Start-Sleep -Milliseconds 100
    }
    throw $Message
}
try {
    Wait-Until { $process.Refresh(); $process.MainWindowHandle -ne 0 } 'No UI window.'
    $window = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    $buttonType = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonType)
    $mount = $buttons | Where-Object { $_.Current.AutomationId -eq 'mountButton' } | Select-Object -First 1
    if (!$mount) { throw 'Mount button not found.' }
    ([System.Windows.Automation.InvokePattern]$mount.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-Until { Test-Path L:\samples\sample.bin } 'UI mount did not complete.'
    if ((Get-Item L:\samples\sample.bin).Length -ne 16777216) { throw 'Wrong file size.' }
    if ($mount.Current.IsEnabled) { throw 'Mount button stayed enabled while mounted.' }
    $unmount = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonType) |
        Where-Object { $_.Current.AutomationId -eq 'unmountButton' } | Select-Object -First 1
    if (!$unmount -or !$unmount.Current.IsEnabled) { throw 'Unmount unavailable.' }
    ([System.Windows.Automation.InvokePattern]$unmount.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-Until { !(Test-Path L:\) -and $mount.Current.IsEnabled } 'UI unmount did not finish.'
    ([System.Windows.Automation.InvokePattern]$mount.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-Until { Test-Path L:\samples\sample.bin } 'Second UI mount failed.'
    $null = $process.CloseMainWindow()
    Wait-Until { $process.HasExited -and !(Test-Path L:\) } 'Closing UI did not unmount.'
    'UI mount, unmount, remount and close-while-mounted passed.'
}
finally {
    if (!$process.HasExited) {
        $null = $process.CloseMainWindow()
        if (!$process.WaitForExit(15000)) { Stop-Process -Id $process.Id -Force }
    }
    $process.Dispose()
}
