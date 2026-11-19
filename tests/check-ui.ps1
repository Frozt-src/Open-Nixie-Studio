param([string]$CaptureDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::EnableVisualStyles()
$exe = Join-Path (Split-Path $PSScriptRoot -Parent) 'out\NixieStudio.exe'
$app = [Reflection.Assembly]::LoadFrom($exe)
$clock = [Activator]::CreateInstance($app.GetType('ClockEngine',$true))
$form = [Activator]::CreateInstance($app.GetType('ClockWindow',$true),@($clock))
if ($CaptureDirectory) { [void][IO.Directory]::CreateDirectory($CaptureDirectory) }
function Settle {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 12 } until ($watch.ElapsedMilliseconds -gt 220)
}
function Activate([string]$name) {
    $access = $surface.AccessibilityObject
    $target = @(0..($access.GetChildCount()-1) | ForEach-Object { $access.GetChild($_) } | Where-Object Name -eq $name)[0]
    if (!$target) { throw "Missing target $name" }
    $target.DoDefaultAction()
    Settle
}
function Capture([string]$name) {
    if (!$CaptureDirectory) { return }
    $image = New-Object Drawing.Bitmap $surface.Width,$surface.Height
    try {
        $surface.DrawToBitmap($image,$surface.ClientRectangle)
        $image.Save((Join-Path $CaptureDirectory "$name.png"),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $image.Dispose() }
}
try {
    $form.Show()
    $surface = $form.Controls[0]
    $groups = @{'Display'=4;'Sound'=2;'Alarms & schedules'=4;'Device'=2}
    foreach ($size in @([Drawing.Size]::new(1497,966),[Drawing.Size]::new(1100,710),[Drawing.Size]::new(860,570))) {
        $form.ClientSize=$size
        Settle
        if ($form.Region.IsVisible(0,0) -or !$form.Region.IsVisible(24,24)) { throw 'Window corner clipping failed.' }
        foreach ($name in 'Display','Sound','Alarms & schedules','Device','Overview') {
            Activate $name
            if ($name -ne 'Overview') {
                $hostPanel=$surface.Controls[0]
                $grid=$hostPanel.Controls[0]
                if (!$grid.Visible -or $grid.Controls.Count -ne $groups[$name]) { throw "Incorrect card count on $name" }
                if ($hostPanel.HorizontalScroll.Visible) { throw "Horizontal overflow on $name" }
                $expectedColumns=if ($size.Width -eq 860) { 1 } else { 2 }
                $columns=@($grid.Controls | ForEach-Object Left | Sort-Object -Unique).Count
                if ($columns -ne $expectedColumns) { throw "Found $columns columns on $name at $size" }
                if ($grid.Height -gt $hostPanel.Height) {
                    if (!$hostPanel.ScrollKey([Windows.Forms.Keys]::End) -or $grid.Bottom -ne $hostPanel.ClientSize.Height) { throw "Last card cannot scroll fully into view on $name" }
                    [void]$hostPanel.ScrollKey([Windows.Forms.Keys]::Home)
                }
                foreach ($group in $grid.Controls) {
                    if ($group.Width -lt 230 -or $group.Height -lt 80) { throw "Collapsed card on $name" }
                    foreach ($child in $group.Controls) {
                        if ($child.Bottom -gt $group.Height-10 -or $child.Left -lt 10) { throw "Child escapes card inset on $name" }
                    }
                }
            }
            Capture "$($size.Width)-$name"
            Write-Output "PASS  $name at $($size.Width) x $($size.Height)"
        }
    }
    $form.WindowState=[Windows.Forms.FormWindowState]::Maximized
    Settle
    if ($null -ne $form.Region) { throw 'Maximized window still has cut corners.' }
    $form.WindowState=[Windows.Forms.FormWindowState]::Normal
    Settle
    if ($null -eq $form.Region -or $form.Region.IsVisible(0,0)) { throw 'Restored window lost rounded corners.' }
    foreach ($name in 'Nightfall','Focus','Daylight') {
        Activate $name
        $image=New-Object Drawing.Bitmap $surface.Width,$surface.Height
        try {
            $surface.DrawToBitmap($image,$surface.ClientRectangle)
            foreach ($item in @('Daylight','Nightfall','Focus')) {
                $index=@('Daylight','Nightfall','Focus').IndexOf($item)
                $pixel=$image.GetPixel([int]((1171+117*$index-45)*$surface.Width/1497),[int]((740-12)*$surface.Height/966))
                $teal=$pixel.G-$pixel.R -gt 45
                if ($teal -ne ($name -eq $item)) { throw "Incorrect active scene fill for $name" }
            }
        } finally { $image.Dispose() }
    }
    foreach ($name in 'Seconds','Seconds','24-hour','24-hour') { Activate $name }
    if ($clock.PortOpen) { throw 'Preview opened a transport.' }
    Write-Output 'PASS  Scenes, preview switches and rounded window region'
} finally { $form.Dispose(); $clock.Dispose() }
