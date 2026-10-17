# Private runtime: no system Python, PATH changes, service registration or admin rights
param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Fetch($Url, $Path, $Hash) {
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Path -TimeoutSec 180
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Hash) {
        Remove-Item -LiteralPath $Path -Force
        throw 'Translator download verification failed. Retry setup.'
    }
}
$runtime = @(Get-Content -LiteralPath (Join-Path $Root 'runtime.txt'))
if ($runtime.Count -ne 5 -or $runtime[1] -ne 'python.exe' -or $runtime[2] -notmatch '^ready-v\d+$' -or $runtime[3] -notmatch '^ready-v\d+$' -or $runtime[4] -notmatch '^setup-v\d+$') {
    throw 'Translation runtime settings are invalid.'
}
$pythonRoot = Join-Path $Root ('python-' + $runtime[0])
$pythonExe = Join-Path $pythonRoot $runtime[1]
$ready = Join-Path $pythonRoot $runtime[2]
$requirementsHash = (Get-FileHash -LiteralPath (Join-Path $Root 'requirements.txt') -Algorithm SHA256).Hash.ToLowerInvariant()
$fingerprintInput = ($runtime -join "`n") + "`n" + $requirementsHash
$fingerprint = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprintInput)))
if ((Test-Path -LiteralPath $ready) -and (Get-Content -LiteralPath $ready -Raw).Trim() -eq $fingerprint) { exit 0 }
New-Item -ItemType Directory -Force -Path $pythonRoot | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $pythonRoot $runtime[3]))) {
Write-Output 'Downloading the private translation runtime...'
$archive = Join-Path $Root 'python.zip'
Fetch ('https://www.python.org/ftp/python/{0}/python-{0}-embed-amd64.zip' -f $runtime[0]) $archive '009D6BF7E3B2DDCA3D784FA09F90FE54336D5B60F0E0F305C37F400BF83CFD3B'
# overwrite an interrupted setup without deleting user data or models
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try { foreach ($entry in $zip.Entries) { if ($entry.Name) { [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $pythonRoot $entry.FullName), $true) } } } finally { $zip.Dispose() }
Remove-Item -LiteralPath $archive
@('python311.zip', '.', 'Lib\site-packages', 'import site') | Set-Content -LiteralPath (Join-Path $pythonRoot 'python311._pth') -Encoding ASCII
$pip = Join-Path $pythonRoot 'pip-25.0.1-py3-none-any.whl'
Fetch 'https://files.pythonhosted.org/packages/c9/bc/b7db44f5f39f9d0494071bddae6880eb645970366d0a200022a1a93d57f5/pip-25.0.1-py3-none-any.whl' $pip 'C46EFD13B6AA8279F33F2864459C8CE587EA6A1A59EE20DE055868D8F7688F7F'
Write-Output 'Installing translation dependencies. First setup may take several minutes...'
$site = Join-Path $pythonRoot 'Lib/site-packages'
New-Item -ItemType Directory -Force -Path $site | Out-Null
# install pip as vendored wheel contents, avoiding a system Python or get pip script
$pipZip = [IO.Compression.ZipFile]::OpenRead($pip)
try {
    foreach ($entry in $pipZip.Entries) {
        if (-not $entry.Name) { continue }
        $destination = Join-Path $site $entry.FullName
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
    }
} finally { $pipZip.Dispose() }
$buildTools = Start-Process -FilePath $pythonExe -ArgumentList @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', 'setuptools==75.8.0', 'wheel==0.45.1') -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $Root 'build-tools.log') -RedirectStandardError (Join-Path $Root 'build-tools-error.log')
if ($buildTools.ExitCode -ne 0) { throw 'Could not install translation dependencies. Check your connection and retry.' }
Set-Content -LiteralPath (Join-Path $pythonRoot $runtime[3]) -Value '1' -Encoding ASCII
}
$install = Start-Process -FilePath $pythonExe -ArgumentList @('-I', '-m', 'pip', 'install', '--disable-pip-version-check', '--no-warn-script-location', '--upgrade', '--no-build-isolation', '-r', ('"' + (Join-Path $Root 'requirements.txt') + '"')) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $Root 'setup.log') -RedirectStandardError (Join-Path $Root 'setup-error.log')
if ($install.ExitCode -ne 0) { throw 'Translation setup failed. Check your connection and retry. Details: setup-error.log in the translator data folder.' }
# App-local Microsoft C++ runtime: no system wide redistributable installer
Write-Output 'Preparing native translation libraries...'
$vcArchive = Join-Path $Root 'vclibs.appx'
Fetch 'https://download.microsoft.com/download/4/7/c/47c6134b-d61f-4024-83bd-b9c9ea951c25/Microsoft.VCLibs.x64.14.00.Desktop.appx' $vcArchive 'B56A9101F706F9D95F815F5B7FA6EFBAC972E86573D378B96A07CFF5540C5961'
$vcZip = [IO.Compression.ZipFile]::OpenRead($vcArchive)
try {
    foreach ($entry in $vcZip.Entries) {
        if ($entry.FullName -match '^(msvcp140[^/]*|vcruntime140[^/]*|concrt140|vcomp140)\.dll$') {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $pythonRoot $entry.Name), $true)
        }
    }
} finally { $vcZip.Dispose() }
Remove-Item -LiteralPath $vcArchive
$verifyScript = Join-Path $Root 'verify.py'
Set-Content -LiteralPath $verifyScript -Value 'import argostranslate.translate; from libretranslate.detect import Detector' -Encoding ASCII
$verify = Start-Process -FilePath $pythonExe -ArgumentList @('-I', ('"' + $verifyScript + '"')) -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput (Join-Path $Root 'verify.log') -RedirectStandardError (Join-Path $Root 'verify-error.log')
if ($verify.ExitCode -ne 0) { throw 'The translation runtime could not load. Retry setup.' }
Set-Content -LiteralPath $ready -Value $fingerprint -Encoding ASCII
