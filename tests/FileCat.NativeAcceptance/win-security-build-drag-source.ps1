$ErrorActionPreference = 'Stop'
$validation = Join-Path $env:LOCALAPPDATA 'FileCatValidation'
New-Item -ItemType Directory -Path $validation -Force | Out-Null
$source = Join-Path $env:LOCALAPPDATA 'Temp\CompliantDragSource.cs'
$output = Join-Path $validation 'CompliantDragSource.exe'
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
Add-Type -Path $source -OutputAssembly $output -OutputType WindowsApplication -ReferencedAssemblies System.Windows.Forms,System.Drawing
if (!(Test-Path -LiteralPath $output -PathType Leaf)) { throw 'Compliant source compilation failed.' }
