param([Parameter(Mandatory=$true)][string]$DeepCase,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
try {
 function Idle { if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){throw 'Postpone fixture moves while a test instance is running'} }
 Idle
 $case=[IO.Path]::GetFullPath($DeepCase)
 if($case-cne'C:\FileCatDsa20261104R2\batch-r1252\case-1000000-deep'){throw 'Unknown completed deep case'}
 $receiptPath=Join-Path $case 'receipt.json';$r=Get-Content -Raw -LiteralPath $receiptPath|ConvertFrom-Json
 $source='C:\FileCatDsa20261104R2\batch-r1181\retained-fixtures\native-5e515a7094c640bfad6f56a33b2b1d34\source'
 if(!$r.completed-or!$r.cleanup-or$r.failure-or$r.phase-ne'closed'-or$r.count-ne1000000-or$r.shape-ne'deep'-or$r.paths-ne1000033-or$r.sourceCloseFailures-ne0-or$r.remainingSharedQuotaBytes-ne0-or!$r.reusedSource-or[IO.Path]::GetFullPath($r.retainedSource)-cne$source){throw 'Require the matching completed and actually closed deep case'}
 $container=[IO.Path]::GetDirectoryName($source);$marker=Join-Path $container 'fixture.json';$m=Get-Content -Raw -LiteralPath $marker|ConvertFrom-Json
 if($m.kind-ne'FileCat retained native scale fixture v1'-or$m.count-ne1000000-or$m.shape-ne'deep'-or!$m.sourceCreationComplete-or[IO.Path]::GetFullPath($m.source)-cne$source){throw 'Owned retained source marker mismatch'}
 $bottom=$source
 $ancestors=@($source,$container,[IO.Path]::GetDirectoryName($container),'C:\FileCatDsa20261104R2\batch-r1181','C:\FileCatDsa20261104R2',$case)
 for($depth=0;$depth-lt32;$depth++){$bottom=Join-Path $bottom 'd';$ancestors+=$bottom}
 foreach($path in $ancestors){if((Get-Item -LiteralPath $path -Force).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'A fixture ancestor is a reparse point'}}
 $staging=Join-Path $container ('wide-staging-'+[Guid]::NewGuid().ToString('N'))
 $stagedBottom=$staging;for($depth=0;$depth-lt32;$depth++){$stagedBottom=Join-Path $stagedBottom 'd'}
 foreach($path in @($source,$bottom,$staging,$stagedBottom)){
  if(![IO.Path]::GetFullPath($path).StartsWith($container+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture move escaped its explicitly owned container'}
 }
 $next=Join-Path $container 'fixture.next-wide-r1252.json';$backup=Join-Path $container 'fixture.before-wide-r1252.json'
 foreach($path in @($staging,$next,$backup)){if(Test-Path -LiteralPath $path){throw 'Fresh staging and marker names required'}}
 $anchor=Join-Path $source 'anchor.bin';$newAnchor=Join-Path $bottom 'anchor.bin'
 if(!(Test-Path -LiteralPath $anchor -PathType Leaf)-or(Test-Path -LiteralPath $newAnchor)){throw 'Unexpected deep fixture anchor placement'}
 $record=[ordered]@{kind='Owned native fixture flatten v1';phase='prepared';source=$source;staging=$staging;stagedBottom=$stagedBottom;deepCase=$case;priorReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;priorMarkerSha256=(Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash;utc=[DateTime]::UtcNow.ToString('o')}
 $record|ConvertTo-Json|Set-Content -LiteralPath $Output
 Idle
 [IO.File]::Move($anchor,$newAnchor);$record.phase='anchor-moved';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 [IO.Directory]::Move($source,$staging);$record.phase='source-staged';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 [IO.Directory]::Move($stagedBottom,$source);$record.phase='rows-flattened';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 # Only the generated, now empty 32-directory scaffold can be removed. Resolve and
 # constrain the final recursive target again immediately before its native deletion.
 if([IO.Path]::GetFullPath($staging)-cne$record.staging-or!$staging.StartsWith($container+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target changed'}
 foreach($entry in @(Get-Item -LiteralPath $staging -Force)+@(Get-ChildItem -LiteralPath $staging -Recurse -Force)){
  if(!$entry.PSIsContainer-or$entry.Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'Unexpected entry in empty fixture scaffold'}
 }
 Remove-Item -LiteralPath $staging -Recurse -Force
 [ordered]@{kind=$m.kind;count=1000000;shape='wide';source=$source;sourceCreationComplete=$true;sourceClosureConfirmedByCompletedDeepReceipt=$true;priorReceiptSha256=$record.priorReceiptSha256;originalCase=$case;reshapedFrom='deep';utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $next
 [IO.File]::Replace($next,$marker,$backup)
 $record.phase='complete';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 exit 0
}catch{[ordered]@{error=($_|Out-String);utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath ($Output+'.error.json');exit 1}
