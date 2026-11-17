param([Parameter(Mandatory=$true)][string]$PriorBatch,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
try {
 if(@(Get-Process -Name 'FileCat*',dotnet -ErrorAction SilentlyContinue).Count){throw 'Postpone reshaping while any FileCat or test instance is running'}
 $batch=[IO.Path]::GetFullPath($PriorBatch)
 if($batch-notmatch'^C:\\FileCatDsa20261104R2\\batch-r[0-9]+[a-z]?$'){throw 'Unknown owned batch'}
 $case=Join-Path $batch 'case-1000000-wide';$receiptPath=Join-Path $case 'receipt.json'
 $r=[IO.File]::ReadAllText($receiptPath)|ConvertFrom-Json
 if(!$r.completed-or!$r.cleanup-or$r.failure-or$r.phase-ne'closed'-or$r.count-ne1000000-or$r.shape-ne'wide'-or$r.paths-ne1000001-or$r.sourceCloseFailures-ne0-or$r.remainingSharedQuotaBytes-ne0){throw 'The wide case must have completed and actually closed every owner'}
 $source=[IO.Path]::GetFullPath($r.retainedSource)
 $container=[IO.Path]::GetDirectoryName($source);$retained=[IO.Path]::GetDirectoryName($container);$originalBatch=[IO.Path]::GetDirectoryName($retained)
 if([IO.Path]::GetFileName($source)-cne'source'-or[IO.Path]::GetFileName($container)-cnotmatch'^native-[0-9a-f]{32}$'-or[IO.Path]::GetFileName($retained)-cne'retained-fixtures'-or$originalBatch-notmatch'^C:\\FileCatDsa20261104R2\\batch-r[0-9]+[a-z]?$'){throw 'Unknown retained source boundary'}
 foreach($path in @($source,$container,$retained,$originalBatch,$batch,$case)){
  if((Get-Item -LiteralPath $path -Force).Attributes-band[IO.FileAttributes]::ReparsePoint){throw 'A fixture ancestor is a reparse point'}
 }
 $marker=Join-Path $container 'fixture.json';$m=[IO.File]::ReadAllText($marker)|ConvertFrom-Json
 if($m.kind-ne'FileCat retained native scale fixture v1'-or$m.count-ne1000000-or$m.shape-ne'wide'-or[IO.Path]::GetFullPath($m.source)-ne$source){throw 'Retained source marker mismatch'}
 $staging=Join-Path $container ('deep-staging-'+[Guid]::NewGuid().ToString('N'));$bottom=$staging
 for($depth=0;$depth-lt31;$depth++){$bottom=Join-Path $bottom 'd'}
 $target=[IO.Path]::GetFullPath((Join-Path $bottom 'd'))
 # Both recursive namespace moves are resolved and constrained before either is issued.
 foreach($path in @($source,$staging,$target)){
  if(![IO.Path]::GetFullPath($path).StartsWith($container+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture move escaped its explicit owned container'}
 }
 if(Test-Path -LiteralPath $staging){throw 'Fresh staging root required'}
 $record=[ordered]@{kind='Owned native fixture reshape v1';phase='prepared';source=$source;staging=$staging;target=$target;priorCase=$case;priorReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash;utc=[DateTime]::UtcNow.ToString('o')}
 $record|ConvertTo-Json|Set-Content -LiteralPath $Output
 New-Item -ItemType Directory -Path $bottom -Force|Out-Null
 [IO.Directory]::Move($source,$target)
 $record.phase='rows-moved';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 [IO.File]::Move((Join-Path $target 'anchor.bin'),(Join-Path $staging 'anchor.bin'))
 [IO.Directory]::Move($staging,$source)
 $next=Join-Path $container 'fixture.next.json';$backup=Join-Path $container 'fixture.before-deep.json'
 if((Test-Path -LiteralPath $next)-or(Test-Path -LiteralPath $backup)){throw 'Fresh marker publication names required'}
 [ordered]@{kind=$m.kind;count=1000000;shape='deep';source=$source;sourceCreationComplete=$true;sourceClosureConfirmedByCompletedWideReceipt=$true;priorReceiptSha256=$record.priorReceiptSha256;originalCase=$case;reshapedFrom='wide';utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $next
 [IO.File]::Replace($next,$marker,$backup)
 $record.phase='complete';$record|ConvertTo-Json|Set-Content -LiteralPath $Output
 exit 0
}catch{[ordered]@{error=($_|Out-String);utc=[DateTime]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath ($Output+'.error.json');exit 1}
