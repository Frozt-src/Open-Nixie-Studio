param([Parameter(Mandatory=$true)][string]$Port, [switch]$VerifyWrite)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'out\NixieStudio.exe'
$app = [Reflection.Assembly]::LoadFrom($exe)
$clock = [Activator]::CreateInstance($app.GetType('ClockEngine',$true))
function Wait-ForSettings {
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $clock.Poll()
        if ($clock.Ready) { return }
        if (!$clock.Busy -or [DateTime]::UtcNow -gt $deadline) { throw $clock.Status }
        Start-Sleep -Milliseconds 30
    } while ($true)
}
try {
    $clock.SelectPort($Port)
    Wait-ForSettings
    Write-Output "PASS  Complete settings snapshot from the selected clock."
    if ($VerifyWrite) {
        $value = $clock.Get(19,0).C
        $clock.Field(19,2,$value,0)
        Wait-ForSettings
        if ($clock.Get(19,0).C -ne $value -or $clock.Status -ne 'Setting confirmed by readback.') { throw $clock.Status }
        Write-Output "PASS  Hour-format write acknowledged and independently read back at its original value."
    }
} finally { $clock.Dispose() }

