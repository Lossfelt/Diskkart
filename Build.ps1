[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'Fant ikke C#-kompilatoren for .NET Framework (Framework64 v4.0.30319).'
}

$icon = Join-Path $repoRoot 'assets\Diskkart.ico'
if (!(Test-Path -LiteralPath $icon)) {
    $iconGeneratorSource = Join-Path $repoRoot 'assets\BuildIcon.cs'
    if (!(Test-Path -LiteralPath $iconGeneratorSource)) {
        throw 'Mangler assets\Diskkart.ico og assets\BuildIcon.cs.'
    }
    $temporaryGenerator = Join-Path ([IO.Path]::GetTempPath()) ('Diskkart-icon-' + [Guid]::NewGuid().ToString('N') + '.exe')
    try {
        & $compiler /nologo /target:exe /optimize+ "/out:$temporaryGenerator" /reference:System.Drawing.dll $iconGeneratorSource
        if ($LASTEXITCODE -ne 0) { throw 'Kompilering av ikongeneratoren feilet.' }
        & $temporaryGenerator $icon
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $icon)) { throw 'Ikongeneratoren opprettet ikke assets\Diskkart.ico.' }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryGenerator) { Remove-Item -LiteralPath $temporaryGenerator -Force }
    }
}

$sources = @(
    'src\Scanner.cs',
    'src\VolumeUsage.cs',
    'src\UiSettings.cs',
    'src\AssemblyInfo.cs',
    'src\Program.cs'
) | ForEach-Object { Join-Path $repoRoot $_ }
foreach ($source in $sources) {
    if (!(Test-Path -LiteralPath $source)) { throw "Mangler kildefil: $source" }
}

$output = Join-Path $repoRoot 'Diskkart.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" "/win32icon:$icon" "/resource:$icon,Diskkart.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Byggingen feilet.' }
Get-Item -LiteralPath $output
