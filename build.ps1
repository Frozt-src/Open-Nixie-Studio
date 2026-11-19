param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Build with Windows PowerShell 5.1 (powershell.exe).'
}
$root = $PSScriptRoot
$out = Join-Path $root 'out'
[void][IO.Directory]::CreateDirectory($out)
function Compile([string]$name, [string]$target, [string[]]$files) {
    $provider = New-Object Microsoft.CSharp.CSharpCodeProvider
    try {
        $options = New-Object CodeDom.Compiler.CompilerParameters
        $options.GenerateExecutable = $true
        $options.OutputAssembly = Join-Path $out $name
        $options.CompilerOptions = "/target:$target /platform:anycpu /optimize+ /debug-"
        $options.ReferencedAssemblies.AddRange([string[]]@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','Accessibility.dll'))
        if ($target -eq 'winexe') {
            $kits = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
            if (!$kits) { $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10' }
            $metadata = Get-ChildItem -LiteralPath (Join-Path $kits 'UnionMetadata') -Directory |
                Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending |
                ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if (!$metadata) { throw 'Install the Windows 10 or 11 SDK to build Bluetooth support.' }
            $options.ReferencedAssemblies.AddRange([string[]]@($metadata,'System.Runtime.dll','System.Runtime.InteropServices.WindowsRuntime.dll','System.Runtime.WindowsRuntime.dll'))
            foreach ($asset in 'Host','Cathodes') {
                $path = Join-Path $root "art\$asset.png"
                $options.CompilerOptions += (' /resource:"{0}",{1}.png' -f $path,$asset)
            }
        }
        $result = $provider.CompileAssemblyFromFile($options, $files)
        if ($result.Errors.HasErrors) { throw ($result.Errors | Out-String) }
    } finally { $provider.Dispose() }
}
if ($Test -or $Package) {
    Compile 'ProtocolTests.exe' 'exe' @((Join-Path $root 'src\Protocol.cs'),(Join-Path $root 'tests\ProtocolTests.cs'))
    & (Join-Path $out 'ProtocolTests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed.' }
}
$source = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
Compile 'NixieStudio.exe' 'winexe' $source
if ($Package) { & (Join-Path $root 'package.ps1') }

