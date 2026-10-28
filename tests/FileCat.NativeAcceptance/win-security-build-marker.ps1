$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'Temp\filecat-security-tests'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$exe = Join-Path $root 'Marker.exe'
$source = @'
using System;
using System.IO;
public static class Marker
{
    public static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathRooted(args[0])) return 2;
        File.WriteAllText(args[0] + ".persisted-launched", Environment.UserName);
        return 0;
    }
}
'@
if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }
Add-Type -TypeDefinition $source -OutputAssembly $exe -OutputType ConsoleApplication
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Marker compilation failed.' }
