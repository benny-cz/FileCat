param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$DotNet,
    [string]$TestBin,
    [ValidateSet('Controller','Owner','Recipient','KeyChanges','ManagedOwner','ManagedRecipient')][string]$Role = 'Controller',
    [string]$AccountSid,
    [switch]$Managed,
    [switch]$DisposableX64Vm
)

$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
if ($Root -notmatch '^[A-Za-z]:\\.+\\filecat-efs-recovery-[0-9a-f]{32}$') {
    throw 'The recovery fixture requires a fresh, uniquely named directory on a local drive.'
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$data = Join-Path $Root 'data'
$testDll = Join-Path $Root 'bin\FileCat.Platform.Windows.Tests.dll'
$source = Join-Path $data 'recovery-source.bin'
$pfx = Join-Path $data 'test-key.pfx'
$managedPfx = Join-Path $data 'managed-agent.pfx'
$managedThumbprint = Join-Path $data 'managed-agent.txt'

function Invoke-Case([string]$Method, [string]$Receipt) {
    $xmlPath = Join-Path $data $Receipt
    & $DotNet $testDll -method "*$Method" -noLogo -xml $xmlPath
    if ($LASTEXITCODE) { throw "Native EFS test exited with code $LASTEXITCODE." }
    [xml]$xml = Get-Content -LiteralPath $xmlPath -Raw
    $assembly = $xml.assemblies.assembly
    $test = $assembly.collection.test
    if ($assembly.total -ne '1' -or $assembly.passed -ne '1' -or $test.result -ne 'Pass' -or $test.name -notlike "*.$Method") {
        throw "The exact native case must execute and pass in $Receipt."
    }
}

function Get-EfsCertificate {
    Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.4.1.311.10.3.4' }
}

# MS-GPEF sections 2.2.1.2.1 and 2.2.1.2.2 define the one-agent policy value.
function New-EfsPolicyBlob($Certificate, $Sid) {
    $der = $Certificate.RawData
    $principal = [byte[]]::new($Sid.BinaryLength)
    $Sid.GetBinaryForm($principal, 0)
    $entryLength = 32 + $principal.Length + $der.Length
    $bytes = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($bytes)
    try {
        $writer.Write([byte[]]@(1,0,1,0))
        $writer.Write([uint32]1)
        $writer.Write([uint32]$entryLength)
        $writer.Write([uint32]($entryLength - 4))
        $writer.Write([uint32]28)
        $writer.Write([uint32]2)
        $writer.Write([uint32]$der.Length)
        $writer.Write([uint32](28 + $principal.Length))
        $writer.Write([uint64]0)
        $writer.Write($principal)
        $writer.Write($der)
        $bytes.ToArray()
    }
    finally { $writer.Dispose(); $bytes.Dispose() }
}

if ($Role -ne 'Controller') {
    $user = $identity.Name.Split('\')[-1]
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if ($identity.User.Value -ne $AccountSid -or !$env:USERPROFILE.EndsWith("\$user", [StringComparison]::OrdinalIgnoreCase) -or
        $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the fixture only under its new standard account with a loaded profile.'
    }
    $env:FILECAT_TEST_EFS = '1'
    if (!$DisposableX64Vm) {
        $env:FILECAT_TEST_WINDOWS_STORAGE_ARM64_JOBS = '1'
        $env:FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE = 'Arm64'
    }
    if ($Role -in 'ManagedOwner','ManagedRecipient') {
        $env:FILECAT_TEST_EFS_MANAGED_RECOVERY_ROOT = $data
        $agentHash = (Get-Content -LiteralPath $managedThumbprint).Trim()
        $managedSource = Join-Path $data 'managed-source.bin'
        if ($Role -eq 'ManagedOwner') {
            Invoke-Case 'Managed_recovery_fixture_encrypts_with_a_reviewed_user_key' 'managed-owner.xml'
            $information = & (Join-Path $env:WINDIR 'System32\cipher.exe') /c $managedSource | Out-String
            if ($LASTEXITCODE) { throw 'Windows could not inspect the managed recovery canary.' }
            $information | Set-Content -LiteralPath (Join-Path $data 'managed-cipher.log')
            $fingerprints = [regex]::Matches($information, '(?im)\b(?:[a-f0-9]{4}[ \t]+){9}[a-f0-9]{4}\b') |
                ForEach-Object { $_.Value -replace '\s', '' }
            if ($agentHash -notin $fingerprints) { throw 'Windows did not attach the provisioned recovery agent to the canary.' }
        } else {
            if (@(Get-EfsCertificate).Count -ne 0) { throw 'The agent account must start without an ordinary EFS key.' }
            $plain = Join-Path $data 'managed-plain-control.bin'
            if ([IO.File]::ReadAllText($plain) -ne 'FileCat managed EFS recovery canary') { throw 'The matching ACL control could not be read.' }
            $denied = $false
            try { [void][IO.File]::ReadAllBytes($managedSource) }
            catch {
                if ($_.Exception.GetBaseException() -is [UnauthorizedAccessException]) { $denied = $true }
                else { throw }
            }
            if (!$denied) { throw 'The agent account read the protected input before importing its unique recovery key.' }
            $password = ConvertTo-SecureString ((Split-Path -Leaf $Root) + 'Managed#2a') -AsPlainText -Force
            $imported = Import-PfxCertificate -FilePath $managedPfx -Password $password -CertStoreLocation Cert:\CurrentUser\My
            if ($imported.Thumbprint -cne $agentHash -or !$imported.HasPrivateKey) { throw 'The managed recovery key did not match the policy.' }
            Invoke-Case 'Managed_recovery_agent_decrypts_the_original_under_an_independent_account' 'managed-agent.xml'
        }
        exit 0
    }
    if ($Role -eq 'KeyChanges') {
        $keys = @(Get-EfsCertificate)
        if ($keys.Count -ne 1 -or $keys[0].Thumbprint -cne (Get-Content -LiteralPath (Join-Path $data 'thumbprint.txt')).Trim()) {
            throw 'Key changes require the original disposable owner profile and its one fixture certificate.'
        }
        $env:FILECAT_TEST_EFS_ROTATE_KEY = '1'
        $env:FILECAT_TEST_EFS_BACKUP_MULTIPLE_KEYS = '1'
        Invoke-Case 'Files_encrypted_with_distinct_real_Efs_keys_keep_independent_backup_choices' 'multiple-keys.xml'
        Invoke-Case 'A_real_key_change_after_setup_requires_review_after_the_current_file_is_encrypted' 'key-change-after-intent.xml'
        Invoke-Case 'An_unexpected_real_key_stops_the_selected_job_and_retains_the_exact_call' 'key-change-stops-job.xml'
        exit 0
    }
    $password = ConvertTo-SecureString ((Split-Path -Leaf $Root) + 'Aa1!') -AsPlainText -Force
    if (@(Get-EfsCertificate).Count -ne 0) { throw 'The fresh fixture account already has an EFS certificate.' }

    if ($Role -eq 'Owner') {
        $env:FILECAT_TEST_EFS_FIRST_USE = '1'
        Invoke-Case 'First_use_in_a_deliberately_fresh_identity_creates_an_actual_key_only_after_explicit_setup' 'first-use.xml'
        [IO.File]::WriteAllText($source, 'synthetic recovery payload')
        Set-Content -LiteralPath $source -Stream fixture -Value 'synthetic recovery stream' -NoNewline -Encoding Ascii
        [IO.File]::Encrypt($source)
        [IO.File]::WriteAllText((Join-Path $data 'plain-control.bin'), 'matching plain ACL control')
        $other = Join-Path $data 'other-user.bin'
        [IO.File]::WriteAllText($other, 'synthetic encrypted control')
        [IO.File]::Encrypt($other)
        $keys = @(Get-EfsCertificate)
        if ($keys.Count -ne 1 -or !$keys[0].HasPrivateKey) { throw 'Expected the fixture owner to have one EFS private key.' }
        Export-PfxCertificate -Cert $keys[0] -FilePath $pfx -Password $password -ChainOption EndEntityCertOnly | Out-Null
        $keys[0].Thumbprint | Set-Content -LiteralPath (Join-Path $data 'thumbprint.txt') -Encoding Ascii
        $AccountSid | Set-Content -LiteralPath (Join-Path $data 'owner-sid.txt') -Encoding Ascii
    } else {
        $env:FILECAT_TEST_EFS_NO_KEY_ROOT = $data
        Invoke-Case 'Another_users_file_with_allowed_ntfs_access_cannot_be_decrypted_without_an_efs_key' 'no-key.xml'
        $denied = $false
        try { [void][IO.File]::ReadAllBytes($source) }
        catch { if ($_.Exception.GetBaseException() -is [UnauthorizedAccessException]) { $denied = $true } else { throw } }
        if (!$denied) { throw 'The recipient could read the recovery source before importing its key.' }
        $restored = Import-PfxCertificate -FilePath $pfx -Password $password -CertStoreLocation Cert:\CurrentUser\My
        if (!$restored.HasPrivateKey -or $restored.Thumbprint -cne (Get-Content -LiteralPath (Join-Path $data 'thumbprint.txt')).Trim()) {
            throw 'The imported key does not match the owner fixture certificate.'
        }
        $env:FILECAT_TEST_EFS_RESTORED_KEY_ROOT = $data
        Invoke-Case 'A_restored_fixture_key_can_decrypt_the_same_file_under_an_independent_windows_account' 'restored-key.xml'
    }
    exit 0
}

if ($DisposableX64Vm) {
    $fixtureProcesses = 'dotnet','testhost','FileCat','FileCat.ShellHost','FileCat.App.Tests','FileCat.*QualificationHost','FileCat.*CrashHarness','nvda'
    $parent = Get-Item -LiteralPath (Join-Path $env:ProgramData 'FileCatValidation')
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (!$Managed -or !$parent.PSIsContainer -or ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        (Split-Path -Parent $Root) -ine $parent.FullName -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64' -or
        [Environment]::OSVersion.Version.Build -lt 22000 -or (Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'VMware' -or
        !$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -or
        (Get-Process $fixtureProcesses -ErrorAction SilentlyContinue)) {
        throw 'Managed recovery requires an idle disposable Windows 11 x64 VMware guest and an administrator fixture controller.'
    }
} else {
    $runnerTemp = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\') + '\'
    if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ARCH -ne 'ARM64' -or
        !$Root.StartsWith($runnerTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use the disposable hosted Windows ARM64 runner.'
    }
}
if (Test-Path -LiteralPath $Root) { throw 'Use a fresh recovery fixture directory.' }
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($Root))
if ($drive.DriveType -ne 'Fixed' -or $drive.DriveFormat -ne 'NTFS') { throw 'The recovery fixture requires fixed NTFS.' }

$users = @()
$credentials = @()
$ownsRoot = $false
$agent = $null
$agentBlob = $null
$policyBlob = $null
$policyTouched = $false
$efsPolicy = 'HKLM:\Software\Policies\Microsoft\SystemCertificates\EFS'
try {
    foreach ($prefix in 'fcEfsSrc_', 'fcEfsRec_') {
        $name = $prefix + [guid]::NewGuid().ToString('N').Substring(0,8)
        $secret = ConvertTo-SecureString ([guid]::NewGuid().ToString('N') + 'Aa1!') -AsPlainText -Force
        $account = New-LocalUser -Name $name -Password $secret -Description 'Disposable FileCat EFS recovery fixture'
        $users += $account
        Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
        $credentials += [pscredential]::new("$env:COMPUTERNAME\$name", $secret)
    }

    $rootInfo = New-Item -ItemType Directory -Path $Root
    $ownsRoot = $true
    $controller = $identity.User.Value
    $owner = $users[0].SID.Value
    $recipient = $users[1].SID.Value
    $controlAcl = "O:${controller}D:P(A;OICI;FA;;;${controller})(A;OICI;FA;;;SY)"
    $security = [Security.AccessControl.DirectorySecurity]::new()
    $sections = [Security.AccessControl.AccessControlSections]'Owner,Access'
    $security.SetSecurityDescriptorSddlForm($controlAcl + "(A;OICI;0x1200a9;;;${owner})(A;OICI;0x1200a9;;;${recipient})", $sections)
    $rootInfo.SetAccessControl($security)
    $dataInfo = New-Item -ItemType Directory -Path $data
    $security.SetSecurityDescriptorSddlForm($controlAcl + "(A;OICI;0x1301bf;;;${owner})(A;OICI;0x1301bf;;;${recipient})", $sections)
    $dataInfo.SetAccessControl($security)

    if ($Managed) {
        if ((Get-ItemProperty -LiteralPath $efsPolicy -Name EfsBlob -ErrorAction SilentlyContinue) -or
            (Get-ChildItem -LiteralPath "$efsPolicy\Certificates" -ErrorAction SilentlyContinue)) {
            throw 'Managed recovery requires a fresh runner with no existing EFS recovery policy.'
        }
        $agent = New-SelfSignedCertificate -Type Custom -Subject "CN=FileCat managed recovery $($users[1].Name)" `
            -CertStoreLocation Cert:\CurrentUser\My -Provider 'Microsoft Enhanced RSA and AES Cryptographic Provider' `
            -KeyAlgorithm RSA -KeyLength 2048 -KeySpec KeyExchange -KeyExportPolicy Exportable -KeyUsage KeyEncipherment,DataEncipherment `
            -TextExtension '2.5.29.37={text}1.3.6.1.4.1.311.10.3.4.1' -NotAfter (Get-Date).AddDays(2)
        $password = ConvertTo-SecureString ((Split-Path -Leaf $Root) + 'Managed#2a') -AsPlainText -Force
        Export-PfxCertificate -Cert $agent -FilePath $managedPfx -Password $password -ChainOption EndEntityCertOnly | Out-Null
        $agent.Thumbprint | Set-Content -LiteralPath $managedThumbprint -Encoding Ascii
        # MS-GPEF 2.2.1.1 uses a serialized public certificate in the machine policy store.
        $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new([byte[]]$agent.RawData)
        try { $agentBlob = $publicCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::SerializedCert) }
        finally { $publicCertificate.Dispose() }
        $policyBlob = [byte[]](New-EfsPolicyBlob $agent $users[1].SID)
        Add-Type -Path (Join-Path $PSScriptRoot 'EfsRecoveryLocalPolicy.cs')
        if ($DisposableX64Vm -and (Get-Process $fixtureProcesses -ErrorAction SilentlyContinue)) {
            throw 'A test process started before the fixture policy could be applied.'
        }
        $policyTouched = $true
        [EfsRecoveryLocalPolicy]::Update($agent.Thumbprint, $agentBlob, $policyBlob, $true)
        & gpupdate.exe /target:computer /force /wait:30
        if ($LASTEXITCODE) { throw 'Windows could not apply the fixture recovery policy.' }
        $current = Get-ItemPropertyValue -LiteralPath $efsPolicy -Name EfsBlob
        $installed = Get-ItemPropertyValue -LiteralPath "$efsPolicy\Certificates\$($agent.Thumbprint)" -Name Blob
        if ([Convert]::ToBase64String($current) -cne [Convert]::ToBase64String($policyBlob) -or
            [Convert]::ToBase64String($installed) -cne [Convert]::ToBase64String($agentBlob)) {
            throw 'The effective policy does not contain the exact public recovery certificate and key record.'
        }
    }

    $action = Join-Path $Root 'fixture.ps1'
    Copy-Item -LiteralPath $PSCommandPath -Destination $action
    Copy-Item -LiteralPath $TestBin -Destination (Join-Path $Root 'bin') -Recurse
    $powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $roles = if ($Managed) { 'ManagedOwner','ManagedRecipient' } else { 'Owner','Recipient','KeyChanges' }
    foreach ($childRole in $roles) {
        $accountIndex = if ($childRole -in 'Recipient','ManagedRecipient') { 1 } else { 0 }
        $params = @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',"`"$action`"",'-Root',"`"$Root`"",
            '-DotNet',"`"$DotNet`"",'-Role',$childRole,'-AccountSid',$users[$accountIndex].SID.Value)
        if ($DisposableX64Vm) { $params += '-DisposableX64Vm' }
        $output = Join-Path $Root "$childRole.out.log"
        $errors = Join-Path $Root "$childRole.err.log"
        $child = Start-Process -FilePath $powershell -ArgumentList $params -Credential $credentials[$accountIndex] -LoadUserProfile -WindowStyle Hidden `
            -WorkingDirectory $Root -RedirectStandardOutput $output -RedirectStandardError $errors -PassThru -Wait
        if ($child.ExitCode -ne 0) { Get-Content -LiteralPath $errors; throw "$childRole fixture exited with code $($child.ExitCode)." }
    }
    if ($Managed) { 'Passed encryption under a recovery policy and decryption with the independent recovery agent.' }
    else { 'Passed first use, independent key restore, selection with distinct keys, and default-key change checks.' }
}
finally {
    if ($policyTouched) {
        try {
            [EfsRecoveryLocalPolicy]::Update($agent.Thumbprint, $agentBlob, $policyBlob, $false)
            & gpupdate.exe /target:computer /force /wait:30 | Out-Null
            if ($LASTEXITCODE) { Write-Warning 'Windows did not refresh after removing the fixture recovery policy.' }
            if ((Get-ItemProperty -LiteralPath $efsPolicy -Name EfsBlob -ErrorAction SilentlyContinue) -or
                (Test-Path -LiteralPath "$efsPolicy\Certificates\$($agent.Thumbprint)")) {
                Write-Warning 'The effective policy still contains a recovery record or the fixture certificate.'
            }
        }
        catch { Write-Warning "The disposable runner could not retire its fixture policy. $($_.Exception.Message)" }
    }
    if ($agent) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($agent.Thumbprint)" -DeleteKey -ErrorAction Continue }
    foreach ($account in $users) {
        Remove-LocalUser -InputObject $account -ErrorAction Continue
        if ($DisposableX64Vm) {
            Get-CimInstance Win32_UserProfile -Filter "SID='$($account.SID.Value)' AND Loaded=FALSE" |
                Remove-CimInstance -ErrorAction Continue
        }
    }
    if ($ownsRoot -and (Test-Path -LiteralPath $pfx -PathType Leaf)) { Remove-Item -LiteralPath $pfx }
    if ($ownsRoot -and (Test-Path -LiteralPath $managedPfx -PathType Leaf)) { Remove-Item -LiteralPath $managedPfx }
}
