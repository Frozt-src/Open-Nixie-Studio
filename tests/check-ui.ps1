$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::EnableVisualStyles()
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'out\NixieStudio.exe'
$app = [Reflection.Assembly]::LoadFrom($exe)
$clock = [Activator]::CreateInstance($app.GetType('ClockEngine',$true))
$form = [Activator]::CreateInstance($app.GetType('ClockWindow',$true),@($clock))
try {
    $form.Show()
    [Windows.Forms.Application]::DoEvents()
    $surface = $form.Controls[0]
    $access = $surface.AccessibilityObject
    foreach ($name in 'Display','Sound','Alarms & schedules','Device','Overview') {
        $button = @(0..($access.GetChildCount()-1) | ForEach-Object { $access.GetChild($_) } | Where-Object Name -eq $name)[0]
        if (!$button) { throw "Missing navigation item $name" }
        $button.DoDefaultAction()
        [Windows.Forms.Application]::DoEvents()
        if ($name -ne 'Overview') {
            $grid = $surface.Controls[0].Controls[0]
            if (!$grid.Visible -or $grid.Controls.Count -lt 1) { throw "Empty page $name" }
            foreach ($group in $grid.Controls) {
                if ($group.Width -lt 100 -or $group.Height -lt 65) { throw "Collapsed group on $name" }
            }
        }
        Write-Output "PASS  $name"
    }
    if ($clock.PortOpen) { throw 'Preview opened a transport.' }
    if (($app.GetManifestResourceNames() -join ' ') -notmatch 'Cathodes.png') { throw 'Missing embedded tube artwork.' }
    Write-Output 'PASS  Offline launch with embedded artwork'
} finally { $form.Dispose(); $clock.Dispose() }

