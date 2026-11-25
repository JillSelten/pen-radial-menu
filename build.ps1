# Bouwt Pen Radial Menu met de C#-compiler die in Windows zit (.NET Framework 4.8).
#
#   powershell -ExecutionPolicy Bypass -File build.ps1
#   powershell -ExecutionPolicy Bypass -File build.ps1 -CertPfx mijncert.pfx -CertPassword geheim
#
# Resultaat in dist\:
#   Pen Radial Menu.exe        - de app zelf
#   Pen Radial Menu Setup.exe  - installatieprogramma (bevat de app)
#
# Met -CertPfx worden beide bestanden digitaal ondertekend (Authenticode, SHA-256,
# met tijdstempel), zodat Windows je naam als uitgever toont.
param(
    [string]$CertPfx,
    [string]$CertPassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',
    [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate   # alternatief voor -CertPfx
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here 'dist'
New-Item -ItemType Directory -Force $out | Out-Null

# ---- 1. App-icoon tekenen (meerdere formaten, PNG in ICO)
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $here 'app.ico'
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
$fontName = 'Segoe Fluent Icons'
if (-not ((New-Object System.Drawing.Text.InstalledFontCollection).Families.Name -contains $fontName)) { $fontName = 'Segoe MDL2 Assets' }
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $r = New-Object System.Drawing.RectangleF 0, 0, ($s - 1), ($s - 1)
    $rad = [Math]::Max(2, $s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $path.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $path.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $path.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $r, ([System.Drawing.Color]::FromArgb(255, 0x95, 0xA3, 0xFF)), ([System.Drawing.Color]::FromArgb(255, 0x5B, 0x6C, 0xF0)), 45
    $g.FillPath($brush, $path)
    $font = New-Object System.Drawing.Font $fontName, ([float]($s * 0.52)), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $g.DrawString([string][char]0xE8A9, $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, ($s * 0.03), $s, $s), $sf)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    $bmp.Dispose()
}
$fs = [System.IO.File]::Create($ico)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]$len); $bw.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close()

# ---- 2. Manifest (scherp op hoge DPI, geen beheerdersrechten)
$manifest = Join-Path $here 'app.manifest'
@'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="PenMapper.app"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security><requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
      <requestedExecutionLevel level="asInvoker" uiAccess="false"/>
    </requestedPrivileges></security>
  </trustInfo>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true</dpiAware>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application><supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/></application>
  </compatibility>
  <dependency><dependentAssembly>
    <assemblyIdentity type="win32" name="Microsoft.Windows.Common-Controls" version="6.0.0.0" processorArchitecture="*" publicKeyToken="6595b64144ccf1df" language="*"/>
  </dependentAssembly></dependency>
</assembly>
'@ | Set-Content -Encoding UTF8 $manifest

# ---- 3. Ondertekenen (optioneel)
$cert = $Certificate
if (-not $cert -and $CertPfx) {
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 ((Resolve-Path $CertPfx).Path), $CertPassword, $flags
}
function Sign-File([string]$file) {
    if (-not $cert) { return }
    $params = @{ FilePath = $file; Certificate = $cert; HashAlgorithm = 'SHA256' }
    if ($TimestampUrl -and $TimestampUrl -ne 'none') { $params.TimestampServer = $TimestampUrl }   # 'none' = zonder tijdstempel
    $sig = Set-AuthenticodeSignature @params
    Write-Host ("ondertekend: {0}  ({1})" -f (Split-Path -Leaf $file), $sig.Status)
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'

# ---- 4. App compileren
$exe = Join-Path $out 'Pen Radial Menu.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$exe" "/win32icon:$ico" "/win32manifest:$manifest" "/resource:$ico,PenMapper.app.ico" `
    $refs (Get-ChildItem $here -Filter *.cs | Where-Object { $_.Name -ne 'Setup.cs' } | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { throw 'Compileren van de app mislukt' }
Sign-File $exe

# ---- 5. Setup compileren (met de app erin)
$setup = Join-Path $out 'Pen Radial Menu Setup.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    "/out:$setup" "/win32icon:$ico" "/win32manifest:$manifest" "/resource:$ico,PenMapper.app.ico" "/resource:$exe,PenMapper.payload.exe" `
    $refs (Join-Path $here 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compileren van de setup mislukt' }
Sign-File $setup

Get-ChildItem $out -Filter *.exe | Select-Object Name, @{n = 'KB'; e = { [int]($_.Length / 1KB) } }
