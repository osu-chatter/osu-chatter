[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ReportPath,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$RunUrl = '',
    [switch]$DryRun,
    [string]$OutputPath = ''
)
$ErrorActionPreference = 'Stop'
$report = Get-Content -LiteralPath $ReportPath -Raw | ConvertFrom-Json
if ($report.schema_version -ne 1 -or $report.status -notin @('matching', 'review-required', 'baseline-unverified')) {
    throw 'Invalid stable update report.'
}
if ($report.status -ne 'review-required') {
    Write-Output 'No version mismatch; no issue needed.'
    return
}
if ($report.fixture -and -not $DryRun) { throw 'Saved changelog fixtures cannot create GitHub issues. Use -DryRun to preview.' }
$latest = $report.latest.version
$baseline = $report.baseline
if ($latest -notmatch '^[0-9]{8}(\.[0-9]+)?$' -or $report.latest.stream -cne 'stable40' -or $baseline.stream -cne 'stable40' -or
    [string]::IsNullOrWhiteSpace($baseline.osu_build) -or $latest -ceq $baseline.osu_build) {
    throw 'Expected a changed stable40 build with a verified baseline.'
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Set Repository to owner/repository.' }
$serverUrl = if ($env:GITHUB_SERVER_URL) { $env:GITHUB_SERVER_URL.TrimEnd('/') } else { 'https://github.com' }
$apiUrl = if ($env:GITHUB_API_URL) { $env:GITHUB_API_URL.TrimEnd('/') } else { 'https://api.github.com' }
if (-not $RunUrl -and $env:GITHUB_RUN_ID) { $RunUrl = "$serverUrl/$Repository/actions/runs/$env:GITHUB_RUN_ID" }
$marker = "<!-- osu-chatter:stable-update:$latest -->"
$title = "osu!stable ${latest}: integration review required"
$runLink = if ($RunUrl) { "[Workflow run]($RunUrl)" } else { 'Local preview' }
$fixtureNote = if ($report.fixture) { "`nFixture preview; no update was detected.`n" } else { '' }
$body = @"
$marker
osu!stable changed from **$($baseline.osu_build)** to **$latest**. Integration needs verification.
$fixtureNote
- [ ] Check reading and sending.
- [ ] Check native translations/restoration, /t and overlay replies.
- [ ] Fix the integration if needed, then update the profile after verification.

[Changelog](https://osu.ppy.sh/home/changelog?stream=stable40) | $runLink | [Profile]($serverUrl/$Repository/blob/main/docs/stable-profile.json)
"@
if ($OutputPath) { [IO.File]::WriteAllText($OutputPath, $body, (New-Object Text.UTF8Encoding($false))) }
if ($DryRun) {
    Write-Output $title
    Write-Output $body
    return
}
if ([string]::IsNullOrWhiteSpace($Token)) { throw 'GITHUB_TOKEN with issues:write is required.' }
$headers = @{ 'User-Agent' = 'osu-chatter-update-watch'; 'Accept' = 'application/vnd.github+json'; 'Authorization' = "Bearer $Token" }
$issuesUrl = "$apiUrl/repos/$Repository/issues"
for ($page = 1; ; $page++) {
    $pageResponse = Invoke-RestMethod -Uri "${issuesUrl}?state=all&per_page=100&page=$page" -Headers $headers -TimeoutSec 30
    $issues = @($pageResponse)
    foreach ($issue in $issues) {
        if (-not $issue.pull_request -and $issue.body -and $issue.body.Contains($marker)) {
            Write-Output ("Issue already exists: " + $issue.html_url)
            return
        }
    }
    if ($issues.Count -lt 100) { break }
}
$payload = @{ title = $title; body = $body } | ConvertTo-Json -Depth 4
$created = Invoke-RestMethod -Uri $issuesUrl -Method Post -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($payload)) -TimeoutSec 30
Write-Output ("Created update review issue: " + $created.html_url)
