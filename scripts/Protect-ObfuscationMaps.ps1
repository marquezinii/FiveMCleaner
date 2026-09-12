[CmdletBinding(DefaultParameterSetName = 'Protect')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Protect')]
    [string]$MappingDirectory,

    [Parameter(Mandatory, ParameterSetName = 'Protect')]
    [string]$OutputPath,

    [Parameter(Mandatory, ParameterSetName = 'Unprotect')]
    [string]$EncryptedPath,

    [Parameter(Mandatory, ParameterSetName = 'Unprotect')]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [string]$EncryptionKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$magic = [Text.Encoding]::ASCII.GetBytes('RVMAP001')
$nonceLength = 12
$tagLength = 16
try {
    $key = [Convert]::FromBase64String($EncryptionKey)
}
catch {
    throw 'The obfuscation-map encryption key must be valid Base64.'
}
if ($key.Length -ne 32) {
    throw 'The obfuscation-map encryption key must decode to exactly 32 bytes.'
}

$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $temporaryBase ("ralven-maps-" + [Guid]::NewGuid().ToString('N'))))
if (-not $temporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Could not create a safe temporary path for the obfuscation maps.'
}
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

try {
    if ($PSCmdlet.ParameterSetName -eq 'Protect') {
        $mappingRoot = [IO.Path]::GetFullPath($MappingDirectory)
        if (-not (Test-Path -LiteralPath $mappingRoot -PathType Container)) {
            throw "Obfuscation mapping directory not found: $mappingRoot"
        }
        $mappings = @(Get-ChildItem -LiteralPath $mappingRoot -Filter 'Mapping-*.txt' -File)
        if ($mappings.Count -eq 0) {
            throw "No Obfuscar mapping files were found in $mappingRoot."
        }

        $archive = Join-Path $temporaryRoot 'mappings.zip'
        Compress-Archive -LiteralPath $mappings.FullName -DestinationPath $archive -CompressionLevel Optimal
        $plainText = [IO.File]::ReadAllBytes($archive)
        $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes($nonceLength)
        $tag = [byte[]]::new($tagLength)
        $cipherText = [byte[]]::new($plainText.Length)
        $aes = [Security.Cryptography.AesGcm]::new($key, $tagLength)
        try {
            $aes.Encrypt($nonce, $plainText, $cipherText, $tag, $magic)
            $verified = [byte[]]::new($plainText.Length)
            $aes.Decrypt($nonce, $cipherText, $tag, $verified, $magic)
            if ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($verified)) -ne
                [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($plainText))) {
                throw 'Encrypted obfuscation-map bundle failed its round-trip integrity check.'
            }
        }
        finally {
            $aes.Dispose()
        }

        $protected = [byte[]]::new($magic.Length + $nonce.Length + $tag.Length + $cipherText.Length)
        [Array]::Copy($magic, 0, $protected, 0, $magic.Length)
        [Array]::Copy($nonce, 0, $protected, $magic.Length, $nonce.Length)
        [Array]::Copy($tag, 0, $protected, $magic.Length + $nonce.Length, $tag.Length)
        [Array]::Copy($cipherText, 0, $protected, $magic.Length + $nonce.Length + $tag.Length, $cipherText.Length)

        $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedOutput) | Out-Null
        [IO.File]::WriteAllBytes($resolvedOutput, $protected)
        Write-Host "Protected obfuscation maps: $resolvedOutput" -ForegroundColor Green
        return
    }

    $resolvedInput = [IO.Path]::GetFullPath($EncryptedPath)
    if (-not (Test-Path -LiteralPath $resolvedInput -PathType Leaf)) {
        throw "Protected obfuscation-map bundle not found: $resolvedInput"
    }
    $resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $resolvedOutputDirectory) {
        throw "Refusing to extract mappings into an existing path: $resolvedOutputDirectory"
    }

    $protected = [IO.File]::ReadAllBytes($resolvedInput)
    $headerLength = $magic.Length + $nonceLength + $tagLength
    if ($protected.Length -le $headerLength) {
        throw 'Protected obfuscation-map bundle is truncated.'
    }
    $actualMagic = $protected[0..($magic.Length - 1)]
    if ([Text.Encoding]::ASCII.GetString($actualMagic) -ne [Text.Encoding]::ASCII.GetString($magic)) {
        throw 'Protected obfuscation-map bundle has an unsupported format.'
    }

    $nonce = $protected[$magic.Length..($magic.Length + $nonceLength - 1)]
    $tagStart = $magic.Length + $nonceLength
    $tag = $protected[$tagStart..($tagStart + $tagLength - 1)]
    $cipherText = $protected[$headerLength..($protected.Length - 1)]
    $plainText = [byte[]]::new($cipherText.Length)
    $aes = [Security.Cryptography.AesGcm]::new($key, $tagLength)
    try {
        $aes.Decrypt($nonce, $cipherText, $tag, $plainText, $magic)
    }
    catch [Security.Cryptography.AuthenticationTagMismatchException] {
        throw 'Protected obfuscation maps failed authentication; the key or bundle is incorrect.'
    }
    finally {
        $aes.Dispose()
    }

    $archive = Join-Path $temporaryRoot 'mappings.zip'
    [IO.File]::WriteAllBytes($archive, $plainText)
    Expand-Archive -LiteralPath $archive -DestinationPath $resolvedOutputDirectory
    Write-Host "Recovered obfuscation maps: $resolvedOutputDirectory" -ForegroundColor Green
}
finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($key)
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
