# Run as Administrator
 
# 1. Disable weak ciphers
$ciphersToDisable = @(
    'RC4 128/128',
    'RC4 64/128',
    'RC4 56/128',
    'RC4 40/128',
    'Triple DES 168',
    'DES 56/56',
    'NULL'
)
 
foreach ($cipher in $ciphersToDisable) {
    $path = "HKLM:\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Ciphers\$cipher"
    New-Item $path -Force | Out-Null
    New-ItemProperty -Path $path -Name 'Enabled' -Value 0 -PropertyType DWORD -Force
    Write-Host "Disabled cipher: $cipher"
}
 
# 2. Set cipher suite priority order (TLS 1.2/1.3)
$cipherSuites = @(
    'TLS_AES_256_GCM_SHA384',           # TLS 1.3
    'TLS_AES_128_GCM_SHA256',           # TLS 1.3
    'TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384', # TLS 1.2
    'TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256',
    'TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384',
    'TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256',
    'TLS_ECDHE_ECDSA_WITH_AES_256_CBC_SHA384',
    'TLS_ECDHE_ECDSA_WITH_AES_128_CBC_SHA256',
    'TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA384',
    'TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA256'
) -join ','
 
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002' `
    -Name 'Functions' -Value $cipherSuites -Type String
 
Write-Host "set priority cipher suites"
 
# Set ECC curve order: P-384, P-256, P-521
New-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Cryptography\Configuration\Local\SSL\00010002' `
    -Name 'EccCurves' `
    -Value @('NistP384', 'NistP256', 'NistP521') `
    -PropertyType MultiString `
    -Force

# 3. Set ECC curve priority (P-384 > P-256 > P-521)
$curvePath = 'HKLM:\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002'
$curveOrder = 'NistP384','NistP256','NistP521'
Set-ItemProperty -Path $curvePath -Name 'EccCurves' -Value $curveOrder -Type MultiString
 
Write-Host "Set ECC curve priority order: $($curveOrder -join ', ')"
 
# 4. Restart required
Write-Host "Reboot required for changes to take effect" -ForegroundColor Yellow