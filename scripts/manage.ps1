# Used by the .bat launchers and release workflow. Keep PowerShell 5.1 compatibility.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Build', 'Run', 'Check', 'Package')]
    [string]$Task,
    [switch]$NoPause,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._+-]{0,99}$')]
    [string]$Version = 'local',
    [string]$ArtifactsPath = '',
    [switch]$NoRestore,
    [switch]$ModernTranscriptCheck
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appDirectory = Join-Path $repositoryRoot 'src/Chatter.App/bin/Release/net48'
$testsPath = Join-Path $repositoryRoot 'src/Chatter.Tests/bin/Release/net48/Chatter.Tests.exe'
if ($ArtifactsPath) {
    $ArtifactsPath = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($ArtifactsPath)) { $ArtifactsPath } else { Join-Path $repositoryRoot $ArtifactsPath }))
    $appDirectory = Join-Path $ArtifactsPath 'bin/Chatter.App/release'
    $testsPath = Join-Path $ArtifactsPath 'bin/Chatter.Tests/release/Chatter.Tests.exe'
}
$appPath = Join-Path $appDirectory 'chatter.exe'
$exitCode = 0
$locationPushed = $false

function Assert-Prerequisites {
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'A 64-bit version of Windows is required.'
    }
    $framework = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
    if ($null -eq $framework -or $framework.Release -lt 528040) {
        throw '.NET Framework 4.8 is not installed.'
    }
    if (-not (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
        throw '.NET 10 SDK not found. Download: https://dotnet.microsoft.com/download/dotnet/10.0'
    }
    $sdkOutput = @(& dotnet --version)
    $sdkExitCode = $LASTEXITCODE
    $sdkVersion = $sdkOutput | Select-Object -Last 1
    if ($sdkExitCode -ne 0 -or "$sdkVersion" -notmatch '^([0-9]+)\.' -or [int]$Matches[1] -lt 10) {
        throw "Expected .NET SDK 10 or newer; found '$sdkVersion'."
    }
}

function Build-App {
    Write-Host 'Building Release...'
    $buildArguments = @('build', (Join-Path $repositoryRoot 'chatter.slnx'), '--configuration', 'Release', '--nologo')
    if ($ArtifactsPath) { $buildArguments += @('--artifacts-path', $ArtifactsPath) }
    $buildArguments += @('--disable-build-servers', '-maxcpucount:1', '-p:UseSharedCompilation=false')
    if ($NoRestore) { $buildArguments += '--no-restore' }
    if ($Version -ne 'local') { $buildArguments += ('-p:Version=' + $Version.TrimStart('v')) }
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'Build failed. For MSB3644, install the .NET Framework 4.8 Developer Pack. For locked files, close chatter and retry.'
    }
    if (-not (Test-Path -LiteralPath $appPath) -or -not (Test-Path -LiteralPath "$appPath.config")) {
        throw "Build output missing: expected chatter.exe and chatter.exe.config in $appDirectory"
    }
}

function Test-AppStartup([string]$Executable, [string]$Argument = '--smoke') {
    $smoke = Start-Process -FilePath $Executable -WorkingDirectory (Split-Path -Parent $Executable) -ArgumentList $Argument -WindowStyle Hidden -PassThru
    try {
        if (-not $smoke.WaitForExit(30000)) {
            $smoke.Kill()
            throw 'Startup check timed out after 30 seconds.'
        }
        if ($smoke.ExitCode -ne 0) { throw "Startup check failed (exit $($smoke.ExitCode))." }
    } finally { $smoke.Dispose() }
}

function Check-App {
    Write-Host 'Checking compatibility monitor logic (offline)...'
    & (Join-Path $PSScriptRoot 'test-stable-monitor.ps1')
    Write-Host 'Running tests...'
    & $testsPath
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($ModernTranscriptCheck) {
        & $testsPath '--modern-transcript-test'
        if ($LASTEXITCODE -ne 0) { throw 'Modern transcript integration check failed.' }
    }
    Write-Host 'Checking startup...'
    Test-AppStartup $appPath
    Write-Host 'Checks passed.'
}

function Write-PackagedDocumentation([string]$Source, [string]$Destination, [string[]]$BundledFiles) {
    $content = [IO.File]::ReadAllText($Source)
    $sourceDirectory = Split-Path -Parent $Source
    # Keep bundled links local; repository-only guides/source must open online.
    $content = [regex]::Replace($content, '(?<prefix>!?\[[^\]]*\]\()(?<target>[^)]+)(?<suffix>\))', {
        param($match)
        $target = $match.Groups['target'].Value
        if ($target -match '^(https?://|mailto:|#)') { return $match.Value }
        $parts = $target -split '#', 2
        if ($BundledFiles -contains $parts[0]) { return $match.Value }
        $resolved = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $parts[0]))
        $rootPrefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $resolved)) {
            throw "Invalid documentation link in ${Source}: $target"
        }
        $relative = $resolved.Substring($rootPrefix.Length).Replace('\', '/')
        $url = 'https://github.com/osu-chatter/osu-chatter/blob/main/' + $relative
        if ($parts.Count -gt 1) { $url += '#' + $parts[1] }
        return $match.Groups['prefix'].Value + $url + $match.Groups['suffix'].Value
    })
    $content = $content.Replace('src="assets/chatter-logo.png"', 'src="https://raw.githubusercontent.com/osu-chatter/osu-chatter/main/assets/chatter-logo.png"')
    [IO.File]::WriteAllText($Destination, $content, (New-Object Text.UTF8Encoding($false)))
}

function Package-App {
    $dist = Join-Path $repositoryRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $stage = Join-Path $dist ('osu-chatter-' + $Version)
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    $files = @(
        $appPath
        "$appPath.config"
        (Join-Path $repositoryRoot 'README.md')
        (Join-Path $repositoryRoot 'LICENSE')
        (Join-Path $PSScriptRoot 'START-HERE.txt')
        (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt')
        (Join-Path $repositoryRoot 'RELEASE-NOTES.md')
    )
    Copy-Item -LiteralPath $files -Destination $stage
    $bundledFiles = @($files | ForEach-Object { Split-Path -Leaf $_ }) + @('VERSION.txt')
    Write-PackagedDocumentation (Join-Path $repositoryRoot 'README.md') (Join-Path $stage 'README.md') $bundledFiles
    Write-PackagedDocumentation (Join-Path $repositoryRoot 'RELEASE-NOTES.md') (Join-Path $stage 'RELEASE-NOTES.md') $bundledFiles
    Set-Content -LiteralPath (Join-Path $stage 'VERSION.txt') -Value $Version -Encoding ASCII
    Write-Host 'Checking packaged executable...'
    Test-AppStartup (Join-Path $stage 'chatter.exe')
    if ($ModernTranscriptCheck) { Test-AppStartup (Join-Path $stage 'chatter.exe') '--smoke-modern' }
    $archive = Join-Path $dist 'osu-chatter-windows-x64.zip'
    # Exclude files left in dist by earlier builds.
    $releaseFiles = @('chatter.exe', 'chatter.exe.config', 'README.md', 'LICENSE', 'START-HERE.txt', 'VERSION.txt', 'THIRD-PARTY-NOTICES.txt', 'RELEASE-NOTES.md') |
        ForEach-Object { Join-Path $stage $_ }
    Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -CompressionLevel Optimal -Force
    if ($Version -match '-rc[.\-]') {
        $candidateArchive = Join-Path $dist ('osu-chatter-' + $Version + '-windows-x64.zip')
        Copy-Item -LiteralPath $archive -Destination $candidateArchive -Force
    }
    Write-Host "Created $archive"
}

try {
    Push-Location -LiteralPath $repositoryRoot
    $locationPushed = $true
    Assert-Prerequisites
    Build-App
    switch ($Task) {
        'Build' { Write-Host "Built $appPath" }
        'Run' {
            $app = Start-Process -FilePath $appPath -WorkingDirectory $appDirectory -PassThru
            $app.Dispose()
            Write-Host 'Started chatter.'
        }
        'Check' { Check-App }
        'Package' { Check-App; Package-App }
    }
} catch {
    $exitCode = 1
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    if ($locationPushed) { Pop-Location }
}

if (-not $NoPause -and ($Task -ne 'Run' -or $exitCode -ne 0)) {
    Write-Host ''
    Read-Host 'Press Enter to close' | Out-Null
}
exit $exitCode
