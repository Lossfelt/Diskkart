#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Fant ikke C#-kompilatoren for .NET Framework.' }

# Also makes Test.ps1 usable on a fresh clone, where the generated icon is absent.
& (Join-Path $repoRoot 'Build.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Byggingen før test feilet.' }

$testRoot = Join-Path $repoRoot ('work\test-' + [Guid]::NewGuid().ToString('N'))
$fixture = Join-Path $testRoot 'fixture'
$settings = Join-Path $testRoot 'Diskkart.settings.xml'
$previousSettings = $env:DISKKART_SETTINGS_PATH
New-Item -ItemType Directory -Path $fixture -Force | Out-Null

function Invoke-CSharpTest([string] $name, [string] $mainClass, [string] $testSource, [string[]] $arguments) {
    $exe = Join-Path $testRoot ($name + '.exe')
    $sources = @(
        (Join-Path $repoRoot 'src\Scanner.cs'),
        (Join-Path $repoRoot 'src\VolumeUsage.cs'),
        (Join-Path $repoRoot 'src\UiSettings.cs'),
        (Join-Path $repoRoot 'src\AssemblyInfo.cs'),
        (Join-Path $repoRoot 'src\Program.cs'),
        $testSource
    )
    $icon = Join-Path $repoRoot 'assets\Diskkart.ico'
    & $compiler /nologo /target:exe /platform:x64 "/out:$exe" "/main:$mainClass" "/resource:$icon,Diskkart.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll $sources
    if ($LASTEXITCODE -ne 0) { throw "Kompilering av $name feilet." }
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$name feilet." }
}

try {
    [IO.File]::WriteAllBytes((Join-Path $fixture 'rot.bin'), [byte[]](0..136))
    $child = Join-Path $fixture 'Undermappe æøå'
    New-Item -ItemType Directory -Path $child | Out-Null
    $large = [IO.File]::Open((Join-Path $child 'stor.bin'), [IO.FileMode]::CreateNew)
    try { $large.SetLength(1234567) } finally { $large.Dispose() }
    $deep = $child
    1..8 | ForEach-Object { $deep = Join-Path $deep ('lang undermappe ' + $_); New-Item -ItemType Directory -Path $deep | Out-Null }
    [IO.File]::WriteAllBytes((Join-Path $deep 'null.bin'), [byte[]]@())
    New-Item -ItemType Directory -Path (Join-Path $fixture 'Tom mappe') | Out-Null
    New-Item -ItemType Junction -Path (Join-Path $fixture 'Tilbake til rot') -Target $fixture | Out-Null

    $env:DISKKART_SETTINGS_PATH = $settings
    Invoke-CSharpTest 'ScannerTests' 'ScannerTests' (Join-Path $repoRoot 'src\ScannerTests.cs') @($fixture)
    Invoke-CSharpTest 'UiTests' 'UiTests' (Join-Path $repoRoot 'src\UiTests.cs') @($fixture, (Join-Path $testRoot 'screenshot.png'))
    Invoke-CSharpTest 'AccessTests' 'AccessTests' (Join-Path $repoRoot 'src\AccessTests.cs') @((Join-Path $testRoot 'access'))
    Write-Host 'Alle tester besto.'
}
finally {
    $env:DISKKART_SETTINGS_PATH = $previousSettings
    $expectedParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'work')) + [IO.Path]::DirectorySeparatorChar
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if (!$resolvedTestRoot.StartsWith($expectedParent, [StringComparison]::OrdinalIgnoreCase)) { throw 'Uventet teststi, avbryter opprydding.' }
    $junction = Join-Path $fixture 'Tilbake til rot'
    if (Test-Path -LiteralPath $junction) { Remove-Item -LiteralPath $junction -Force }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force }
}
