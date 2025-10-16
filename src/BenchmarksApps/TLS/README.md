# Useful stuff to test TLS behavior

### Analysis of TLS parameters on request
To lookup TLS behavior you can install npcap/wireshark on win machine, 
and collect a network dump (note: using a custom port requires to use `Analyze->DecodeAs` and set TCP / TLS port on dump data). There in `Client Hello` or `Server Hello` TLS parameters can be found.

However, easier way is to simply perform a curl request
```bash
curl -v https://<ip>:<port>/<endpoint> --tlsv1.3 --tls-max 1.2 --insecure --curves [P-256/P-384/P-521/X25519]
```
where:
- `--insecure` skips certificate check (but still runs with TLS)
- `--tlsv1.3` or `--tlsv1.2` sets a minimum tls version
- `--tls-max 1.3` or `--tls-max 1.2` sets a maximum tls version (does not allow client-server to lift up a version)
- `--curves ...` forces a specific curve.

In output you nee to find SSL connection:
```
* SSL connection using TLSv1.3 / TLS_AES_256_GCM_SHA384 / secp521r1 / RSASSA-PSS
```

### Machine setup
You could use [set-fips-compliant-tls-config](./set-fips-compliant-tls-config.ps1) to configure machine. It may not work (registry on windows does not apply always).

You can set TLS CipherSuite and ECC Curve order in Windows UI:
- Local Group Policy Editor -> Computer Configuration > Administrative Templates > Network > SSL Configuration
- Values can be taken from https://learn.microsoft.com/en-us/windows/win32/secauthn/tls-elliptic-curves-in-windows-10-1607-and-later

### Verify machine setup

#### Windows
- Look cipher suite priority list in registry:
```powershell
(Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Cryptography\Configuration\SSL\00010002' -Name 'Functions').Functions -split ',' | ForEach-Object { "{0,3}. {1}" -f ($_.ReadCount), $_ }
```  

- Look eliptic curves priority list in registry:
```powershell
Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Cryptography\Configuration\Local\SSL\00010002' -Name 'EccCurves' -ErrorAction SilentlyContinue
```
