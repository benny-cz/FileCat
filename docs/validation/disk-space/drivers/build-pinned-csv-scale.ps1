param([Parameter(Mandatory=$true)][ValidatePattern('^r[0-9]+[a-z]?$')][string]$Revision)
$ErrorActionPreference='Stop'
$project=Join-Path $PWD ('.tmp/csv-pinned-'+$Revision);$stage=Join-Path $PWD ('.tmp/csv-scale-'+$Revision)
if((Test-Path -LiteralPath $project)-or(Test-Path -LiteralPath $stage)){throw 'Fresh pinned build and stage required'}
New-Item -ItemType Directory -Path $project|Out-Null
$qualified=Join-Path $PWD '.tmp/csv-scale-r1098'
$core=Join-Path $qualified 'FileCat.Core.dll';$source=Join-Path $PWD 'tests/FileCat.CoreCrashHarness/CsvReportScale.cs'
$csproj=@'
<Project Sdk="Microsoft.NET.Sdk">
 <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><AssemblyName>FileCat.CoreCrashHarness</AssemblyName><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
 <ItemGroup><Reference Include="FileCat.Core"><HintPath>@CORE@</HintPath><Private>false</Private></Reference>
 <Compile Include="@SOURCE@"/><Compile Include="Program.cs"/><Compile Include="Directory.cs"/></ItemGroup>
</Project>
'@
[IO.File]::WriteAllText((Join-Path $project 'PinnedCsv.csproj'),$csproj.Replace('@CORE@',$core).Replace('@SOURCE@',$source),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $project 'Program.cs'),'return await CsvReportScale.RunAsync(int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), args[2], args[3]);',[Text.UTF8Encoding]::new($false))
$directory=@'
using System.Reflection;
using FileCat.Core.Analysis;
internal static class ScratchScale
{
 internal static AnalysisPrivateDirectory Directory(string parent)
 {
  if (!OperatingSystem.IsWindows()) return new UnixAnalysisPrivateDirectory(parent, "scratch");
  var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER") ?? Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll"));
  return (AnalysisPrivateDirectory)Activator.CreateInstance(assembly.GetType("FileCat.Platform.Windows.WindowsAnalysisPrivateDirectory", true)!, parent, "scratch")!;
 }
}
'@
[IO.File]::WriteAllText((Join-Path $project 'Directory.cs'),$directory,[Text.UTF8Encoding]::new($false))
$env:TEMP='E:\FileCat\.tmp\bt-r855';$env:TMP=$env:TEMP;$log='.tmp/csv-pinned-build-'+$Revision+'.txt'
& dotnet build (Join-Path $project 'PinnedCsv.csproj') --disable-build-servers -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -p:NuGetAudit=false -p:FileCatUpdateDependencyLocks=true --ignore-failed-sources *> $log
if($LASTEXITCODE){Get-Content -LiteralPath $log -Tail 20;throw 'Pinned harness compile failed'}
$build=Get-Content -LiteralPath $log -Raw;foreach($value in @('Build succeeded.','0 Warning(s)','0 Error(s)')){if(!$build.Contains($value)){throw 'Clean pinned build required'}}
New-Item -ItemType Directory -Path $stage|Out-Null
Get-ChildItem -LiteralPath $qualified -File|Where-Object Name -ne 'inputs.json'|Copy-Item -Destination $stage
foreach($name in @('FileCat.CoreCrashHarness.dll','FileCat.CoreCrashHarness.pdb')){Copy-Item -LiteralPath (Join-Path $project ('bin/Debug/net10.0/'+$name)) -Destination $stage -Force}
if((Get-FileHash -LiteralPath (Join-Path $stage 'FileCat.Core.dll')).Hash-cne(Get-FileHash -LiteralPath $core).Hash){throw 'Pinned product changed'}
$inputs=@(Get-ChildItem -LiteralPath $stage -File|Sort-Object Name|ForEach-Object{[ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length}})
$inputs|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $stage 'inputs.json')
$archive=Join-Path $PWD ('.tmp/csv-scale-'+$Revision+'.tgz');if(Test-Path -LiteralPath $archive){throw 'Fresh archive required'}
& tar.exe -czf $archive -C $stage .;if($LASTEXITCODE){throw 'Archive failed'}
$sources=@($source,(Join-Path $project 'PinnedCsv.csproj'),(Join-Path $project 'Program.cs'),(Join-Path $project 'Directory.cs'),(Join-Path $project 'packages.lock.json'))
[ordered]@{revision=$Revision;qualifiedProductRevision='r1096';pinnedProductReference=$true;gitBase=(git rev-parse HEAD);buildLog=$log;archive=$archive;sha256=(Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant();bytes=(Get-Item -LiteralPath $archive).Length;inputs=$inputs;
 sources=@($sources|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($PWD,$_).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant()}})}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath ('.tmp/csv-scale-manifest-'+$Revision+'.json')
Write-Output ('Compiled only the harness against the exact qualified Core: '+(Get-Item -LiteralPath $archive).Length+' archive bytes')
