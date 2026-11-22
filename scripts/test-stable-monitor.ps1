[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'check-stable.ps1'
$baseline = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../docs/stable-profile.json') -Raw | ConvertFrom-Json
$manifestFixture = [IO.Path]::GetTempFileName()
$feedFixture = [IO.Path]::GetTempFileName()
$checks = 0
function Test-Case([string]$Name, [scriptblock]$Modify, [string]$ExpectedError = '') {
    $record = $baseline | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $record.osu_build = 'fixture-tested'
    $feed = [pscustomobject]@{ builds = @([pscustomobject]@{ version = 'fixture-tested'; update_stream = [pscustomobject]@{ name = 'stable40' } }) }
    & $Modify $record $feed
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFixture -Encoding UTF8
    $feed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $feedFixture -Encoding UTF8
    $failure = ''
    try { $null = & $checker -Manifest $manifestFixture -ChangelogPath $feedFixture }
    catch { $failure = $_.Exception.Message }
    if (($ExpectedError -and -not $failure.Contains($ExpectedError)) -or (-not $ExpectedError -and $failure)) {
        throw "$Name failed: expected '$ExpectedError', received '$failure'"
    }
    $script:checks++
    Write-Output "ok  monitor: $Name"
}
try {
    Test-Case 'matching recorded build' { }
    Test-Case 'changed upstream build' { param($record, $feed) $feed.builds[0].version = 'fixture-new' } '[review-required]'
    Test-Case 'unverified baseline' { param($record, $feed) $record.osu_build = $null } '[baseline-unverified]'
    Test-Case 'empty feed' { param($record, $feed) $feed.builds = @() } '[feed-invalid]'
    Test-Case 'missing version' { param($record, $feed) $feed.builds[0].version = '' } '[feed-invalid]'
    Test-Case 'wrong upstream stream' { param($record, $feed) $feed.builds[0].update_stream.name = 'lazer' } '[feed-invalid]'
    Test-Case 'wrong manifest stream' { param($record, $feed) $record.stream = 'lazer' } '[manifest-invalid]'
    Test-Case 'unsupported architecture' { param($record, $feed) $record.architecture = 'x64' } '[manifest-invalid]'
    Test-Case 'drifting profile identifier' { param($record, $feed) $record.profile = 'fixture-stale' } '[profile-mismatch]'
    Test-Case 'drifting executable hash' { param($record, $feed) $record.sha256 = '0' * 64 } '[profile-mismatch]'
    $baseline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFixture -Encoding UTF8
    & {
        function Invoke-RestMethod {
            param($Uri, $Headers, $TimeoutSec)
            if ($Uri -cne 'https://osu.ppy.sh/home/changelog?stream=stable40' -or $Headers.Accept -cne 'application/json' -or $TimeoutSec -ne 30) { throw 'Wrong public changelog request' }
            $body = Get-Content -LiteralPath $manifestFixture -Raw | ConvertFrom-Json
            return [pscustomobject]@{ builds = @([pscustomobject]@{ version = $body.osu_build; update_stream = [pscustomobject]@{ name = 'stable40' } }) }
        }
        $null = & $checker -Manifest $manifestFixture
    }
    $checks++
    Write-Output 'ok  monitor: public JSON endpoint and request headers'
    # Mock the transport in a child scope: this case also performs no network request.
    & {
        function Invoke-RestMethod { throw 'fixture HTTP 403' }
        $failure = ''
        try { $null = & $checker -Manifest $manifestFixture }
        catch { $failure = $_.Exception.Message }
        if (-not $failure.Contains('[fetch-failed]')) { throw "fetch failure was not distinguished: $failure" }
    }
    $checks++
    Write-Output 'ok  monitor: fetch failure remains unknown'
    $entryBuild = if ($baseline.osu_build) { $baseline.osu_build } else { 'fixture-tested' }
    [pscustomobject]@{ builds = @([pscustomobject]@{ version = $entryBuild; update_stream = [pscustomobject]@{ name = 'stable40' } }) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $feedFixture -Encoding UTF8
    $entryOutput = @(& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $checker -ChangelogPath $feedFixture 2>&1)
    $entryExit = $LASTEXITCODE
    if (($baseline.osu_build -and $entryExit -ne 0) -or (-not $baseline.osu_build -and ($entryOutput -join ' ') -notmatch '\[baseline-unverified\]')) {
        throw ('Windows PowerShell -File entry point failed: ' + ($entryOutput -join ' '))
    }
    $checks++
    Write-Output 'ok  monitor: Windows PowerShell entry point resolves the default manifest'
    Write-Output "$checks offline monitor checks passed; no live game or network request was used."
} finally {
    Remove-Item -LiteralPath $manifestFixture, $feedFixture -Force
}
