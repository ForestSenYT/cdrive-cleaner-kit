<#
.SYNOPSIS
Signs files with a code-signing certificate from the current user's store and timestamps each signature.

.DESCRIPTION
Used by release.ps1 for the program files, and by Inno Setup for the installer and its uninstaller.
The timestamp keeps a signature valid after the certificate expires; only a hash of the signature is sent to the
timestamp server.

.EXAMPLE
.\build\sign.ps1 -Thumbprint 7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D .\CDriveCleaner.exe
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory)] [string] $Thumbprint,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $Path,
    [string] $TimestampServer = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$certificate = Get-Item "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $certificate -or -not $certificate.HasPrivateKey) {
    throw "当前用户的证书存储里没有指纹为 $Thumbprint 且带私钥的证书。"
}

foreach ($file in $Path) {
    $result = Set-AuthenticodeSignature -FilePath $file -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
    # Windows does not trust a self-signed certificate, so its status reads UnknownError ("root not trusted").
    # What matters here is that the file now carries this certificate's signature and a timestamp.
    $check = Get-AuthenticodeSignature -FilePath $file
    if ($check.SignerCertificate.Thumbprint -ne $Thumbprint -or $check.Status -notin 'Valid', 'UnknownError' -or -not $check.TimeStamperCertificate) {
        throw "签名失败：$file（$($result.Status)：$($result.StatusMessage)）"
    }

    Write-Output "signed: $file"
}
