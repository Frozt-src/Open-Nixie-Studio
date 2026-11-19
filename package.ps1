$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$root = $PSScriptRoot
$release = Join-Path $root 'release'
[void][IO.Directory]::CreateDirectory($release)
# Release contents are explicit so local settings, captures, and git history stay local.
$source = @('.gitignore','README.md','LICENSE','build.ps1','package.ps1','art/Host.png','art/Cathodes.png',
    'src/Bluetooth.cs','src/ClockEngine.cs','src/ClockWindow.cs','src/Protocol.cs','src/StudioSurface.cs',
    'tests/ProtocolTests.cs','tests/check-device.ps1','tests/check-ui.ps1')
function Write-Archive([string]$name,[string[]]$entries) {
    $dest = Join-Path $release $name
    $tmp = Join-Path $release ([guid]::NewGuid().ToString()+'.tmp')
    try {
        $file = [IO.File]::Open($tmp,'CreateNew','Write','None')
        try {
            $zip = New-Object IO.Compression.ZipArchive($file,[IO.Compression.ZipArchiveMode]::Create,$true)
            try {
                foreach ($entry in $entries) {
                    $relative = $entry -replace '^out/',''
                    $item = $zip.CreateEntry($relative)
                    $item.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
                    $inputFile = [IO.File]::OpenRead((Join-Path $root $entry))
                    try {
                        $outputStream = $item.Open()
                        try { $inputFile.CopyTo($outputStream) } finally { $outputStream.Dispose() }
                    } finally { $inputFile.Dispose() }
                }
            } finally { $zip.Dispose() }
        } finally { $file.Dispose() }
        if ([IO.File]::Exists($dest)) { [IO.File]::Replace($tmp,$dest,[System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($tmp,$dest) }
    } finally { if ([IO.File]::Exists($tmp)) { [IO.File]::Delete($tmp) } }
    return $dest
}
$archives = @(
    (Write-Archive 'XGClock-Studio-Windows.zip' @('out/NixieStudio.exe','README.md','LICENSE'))
    (Write-Archive 'XGClock-Studio-source.zip' $source)
)
$hashes = foreach ($zip in $archives) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant(),[IO.Path]::GetFileName($zip)
}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),[string[]]$hashes,[Text.Encoding]::ASCII)
$hashes

