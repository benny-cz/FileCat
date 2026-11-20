<#
.SYNOPSIS
  Signs PE files in place with FileCat's configured signing identity, through the pinned Jsign.
.DESCRIPTION
  Used for payload files (Sign-WindowsPayload.ps1) and as Inno Setup's sign tool for the installer and uninstaller
  (Compile-WindowsInstaller.ps1 -Sign). Every signature is SHA-256 with an RFC 3161 timestamp. Configuration comes from
  the environment, so no secret appears on a command line or in a log:
    FILECAT_SIGN_MODE            TestCertificate | ESignerSandbox | ESigner
    FILECAT_JSIGN_JAR, FILECAT_JAVA   the jar and Java from Acquire-Jsign.ps1 (the jar is re-checked against its pin)
    TestCertificate              FILECAT_SIGN_PFX and FILECAT_SIGN_PFX_PASSWORD (New-TestCodeSigningCertificate.ps1)
    ESignerSandbox, ESigner      FILECAT_ESIGNER_USERNAME, FILECAT_ESIGNER_PASSWORD, FILECAT_ESIGNER_CREDENTIAL_ID and
                                 FILECAT_ESIGNER_TOTP_SECRET (the base64 secret shown with the eSigner QR code)
    FILECAT_SIGN_TSA             optional comma-separated RFC 3161 servers (default SSL.com, then DigiCert)
#>
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$File)
$ErrorActionPreference = 'Stop'
$pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../toolchains/jsign.json') -Raw | ConvertFrom-Json
$jar = $env:FILECAT_JSIGN_JAR
$java = $env:FILECAT_JAVA
if (-not $jar -or -not $java) { throw 'FILECAT_JSIGN_JAR and FILECAT_JAVA must name the jar and Java from Acquire-Jsign.ps1.' }
if ((Get-FileHash -LiteralPath $jar -Algorithm SHA256).Hash.ToLowerInvariant() -cne $pin.JarSHA256) { throw 'The Jsign jar differs from its pin.' }

function Required([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrEmpty($value)) { throw "$Name is not set for FILECAT_SIGN_MODE=$env:FILECAT_SIGN_MODE." }
    $value
}

$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange([string[]]@('-jar', $jar))
switch -CaseSensitive ($env:FILECAT_SIGN_MODE) {
    'TestCertificate' {
        $arguments.AddRange([string[]]@('--storetype', 'PKCS12', '--keystore', (Required 'FILECAT_SIGN_PFX'), '--storepass', 'env:FILECAT_SIGN_PFX_PASSWORD'))
        [void](Required 'FILECAT_SIGN_PFX_PASSWORD')
    }
    { $_ -cin 'ESignerSandbox', 'ESigner' } {
        # Jsign takes the SSL.com username and password as one keystore password, "<username>|<password>". It lives only
        # in this process's environment, which the Java child inherits; the TOTP secret lets Jsign compute each OTP.
        $env:FILECAT_JSIGN_STOREPASS = (Required 'FILECAT_ESIGNER_USERNAME') + '|' + (Required 'FILECAT_ESIGNER_PASSWORD')
        [void](Required 'FILECAT_ESIGNER_TOTP_SECRET')
        $arguments.AddRange([string[]]@('--storetype', 'ESIGNER', '--storepass', 'env:FILECAT_JSIGN_STOREPASS', '--keypass', 'env:FILECAT_ESIGNER_TOTP_SECRET'))
        if ($env:FILECAT_SIGN_MODE -ceq 'ESignerSandbox') { $arguments.AddRange([string[]]@('--keystore', 'https://cs-try.ssl.com')) }
        # Without a credential ID Jsign lists the account's credential IDs and stops, which is how a new account finds it.
        if ($env:FILECAT_ESIGNER_CREDENTIAL_ID) { $arguments.AddRange([string[]]@('--alias', $env:FILECAT_ESIGNER_CREDENTIAL_ID)) }
    }
    default { throw "FILECAT_SIGN_MODE must be TestCertificate, ESignerSandbox or ESigner, not '$env:FILECAT_SIGN_MODE'." }
}
$tsa = if ($env:FILECAT_SIGN_TSA) { $env:FILECAT_SIGN_TSA } else { 'http://ts.ssl.com,http://timestamp.digicert.com' }
$arguments.AddRange([string[]]@('--alg', 'SHA-256', '--tsaurl', $tsa, '--tsmode', 'RFC3161', '--tsretries', '3', '--tsretrywait', '10',
    '--name', 'FileCat', '--url', 'https://github.com/benny-cz/FileCat', '--replace'))

# Jsign recognises PE files by extension, so a file named otherwise (Inno Setup's uninstaller, for one) is signed as
# a temporary .exe copy and copied back over the original.
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('FileCat-jsign-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($scratch)
$renamed = @{}
$list = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($item in $File) {
        $full = (Resolve-Path -LiteralPath $item.Trim('"')).Path
        if ([IO.Path]::GetExtension($full) -in '.exe', '.dll', '.sys') {
            $list.Add($full)
        } else {
            $copy = Join-Path $scratch ("{0}-{1}.exe" -f $renamed.Count, [IO.Path]::GetFileNameWithoutExtension($full))
            Copy-Item -LiteralPath $full -Destination $copy
            $renamed[$copy] = $full
            $list.Add($copy)
        }
    }
    $listFile = Join-Path $scratch 'files.txt'
    [IO.File]::WriteAllLines($listFile, $list)
    $arguments.Add("@$listFile")
    & $java @arguments
    if ($LASTEXITCODE -ne 0) { throw "Jsign failed with exit code $LASTEXITCODE." }
    foreach ($copy in $renamed.Keys) { Copy-Item -LiteralPath $copy -Destination $renamed[$copy] -Force }
} finally {
    Remove-Item Env:\FILECAT_JSIGN_STOREPASS -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}
