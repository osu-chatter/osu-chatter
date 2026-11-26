[CmdletBinding()]
param(
    [string]$Manifest = '',
    # Saved API response for offline checks; omitted in the scheduled job.
    [string]$ChangelogPath = '',
    [string]$ReportPath = ''
)
$ErrorActionPreference = 'Stop'
if ($ReportPath -and (Test-Path -LiteralPath $ReportPath)) { Remove-Item -LiteralPath $ReportPath -Force }
if (-not $Manifest) { $Manifest = Join-Path $PSScriptRoot '../docs/stable-profile.json' }
$profile = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
if ($profile.stream -cne 'stable40' -or $profile.architecture -cne 'x86' -or "$($profile.sha256)" -notmatch '^[A-Fa-f0-9]{64}$') {
    throw '[manifest-invalid] Expected the stable40/x86 profile and an executable SHA-256.'
}
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/Shared/StableCompatibilityProfile.cs') -Raw
$sourceId = [regex]::Match($source, 'const string Id = "([^"]+)"').Groups[1].Value
$sourceHash = [regex]::Match($source, 'const string TestedSha256 = "([^"]+)"').Groups[1].Value
if (-not $sourceId -or -not $sourceHash -or $profile.profile -cne $sourceId -or $profile.sha256 -ine $sourceHash) {
    throw '[profile-mismatch] The manifest identifier/hash must match StableCompatibilityProfile.cs.'
}
if ($ChangelogPath) {
    Write-Output 'Using a saved changelog fixture; no live update check is performed.'
    $response = Get-Content -LiteralPath $ChangelogPath -Raw | ConvertFrom-Json
} else {
    try {
        $response = Invoke-RestMethod -Uri 'https://osu.ppy.sh/home/changelog?stream=stable40' -Headers @{ 'User-Agent' = 'osu-chatter-compatibility-monitor'; 'Accept' = 'application/json' } -TimeoutSec 30
    } catch {
        throw ('[fetch-failed] Cannot check the official changelog. Compatibility is unknown; this does not establish a game incompatibility. ' + $_.Exception.Message)
    }
}
$latest = $response.builds | Select-Object -First 1
if (-not ($latest.version -is [string]) -or [string]::IsNullOrWhiteSpace($latest.version) -or $latest.update_stream.name -cne 'stable40') {
    throw '[feed-invalid] Expected a versioned stable40 build in the official changelog response.'
}
Write-Output ('Latest osu!stable build: ' + $latest.version)
Write-Output ('Recorded compatibility profile: ' + $profile.profile)
if ($ReportPath) {
    $status = if ([string]::IsNullOrWhiteSpace($profile.osu_build)) { 'baseline-unverified' }
        elseif ($profile.osu_build -cne $latest.version) { 'review-required' }
        else { 'matching' }
    [ordered]@{
        schema_version = 1
        status = $status
        fixture = [bool]$ChangelogPath
        checked_at = [DateTime]::UtcNow.ToString('o')
        baseline = $profile
        latest = [ordered]@{
            version = $latest.version
            stream = $latest.update_stream.name
            created_at = $latest.created_at
        }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
if ([string]::IsNullOrWhiteSpace($profile.osu_build)) {
    throw '[baseline-unverified] Only an executable fingerprint is recorded. Verify its official stable40 build before recording osu_build; do not copy the latest version just to pass this check.'
}
if ($profile.osu_build -cne $latest.version) {
    throw ('[review-required] osu!stable ' + $latest.version + ' differs from the recorded build ' + $profile.osu_build + '. Verify reading, sending, native display and command hooks independently; do not automatically change offsets.')
}
Write-Output 'Recorded stable40 build unchanged. This checks update metadata, not live game compatibility.'
