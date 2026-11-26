[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'check-stable.ps1'
$reporter = Join-Path $PSScriptRoot 'report-stable-update.ps1'
$manifestFile = [IO.Path]::GetTempFileName()
$feedFile = [IO.Path]::GetTempFileName()
$reportFile = [IO.Path]::GetTempFileName()
$previewFile = [IO.Path]::GetTempFileName()
$baseline = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../docs/stable-profile.json') -Raw | ConvertFrom-Json
$issueChecks = 0
$requests = New-Object 'System.Collections.Generic.List[object]'
$issuePages = @{}
$apiFailure = ''
function Assert-Issue([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Check-IssueCase([string]$Name, [scriptblock]$Action) {
    & $Action
    $script:issueChecks++
    Write-Output "ok  update issue: $Name"
}
function Save-IssueReport([string]$Status = 'review-required', [bool]$Fixture = $false) {
    [ordered]@{
        schema_version = 1; status = $Status; fixture = $Fixture; checked_at = '2026-11-26T08:00:00Z'
        baseline = $baseline
        latest = @{ version = '20990101.1'; stream = 'stable40'; created_at = '2099-01-01T00:00:00Z' }
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportFile -Encoding UTF8
}
function Invoke-IssueReporter {
    & $reporter -ReportPath $reportFile -Repository 'fixture/project' -Token 'fixture-token' -RunUrl 'https://github.com/fixture/project/actions/runs/1'
}
function Invoke-RestMethod {
    param($Uri, $Headers, $TimeoutSec, $Method = 'Get', $ContentType, $Body)
    $requests.Add([pscustomobject]@{ method = $Method; uri = $Uri; body = $Body })
    if ($apiFailure -eq $Method) { throw "fixture $Method failure" }
    Assert-Issue ($Headers.Authorization -ceq 'Bearer fixture-token') 'Unexpected authentication.'
    if ($Method -eq 'Get') {
        Assert-Issue ($Uri -match '/repos/fixture/project/issues\?state=all&per_page=100&page=(\d+)$') 'Unexpected issue listing request.'
        $pageNumber = [int]$Matches[1]
        $items = if ($issuePages.ContainsKey($pageNumber)) { $issuePages[$pageNumber] } else { @() }
        Write-Output -NoEnumerate @($items)
        return
    }
    Assert-Issue ($Method -eq 'Post' -and $Uri -match '/repos/fixture/project/issues$') 'Unexpected API write.'
    Assert-Issue ($ContentType -eq 'application/json; charset=utf-8') 'Unexpected payload encoding.'
    return [pscustomobject]@{ html_url = 'https://github.com/fixture/project/issues/42' }
}
try {
    $baseline | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFile -Encoding UTF8
    [pscustomobject]@{ builds = @([pscustomobject]@{ version = $baseline.osu_build; update_stream = @{ name = 'stable40' } }) } |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $feedFile -Encoding UTF8
    Check-IssueCase 'matching report is explicitly marked as fixture input' {
        $null = & $checker -Manifest $manifestFile -ChangelogPath $feedFile -ReportPath $reportFile
        $saved = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
        Assert-Issue ($saved.status -eq 'matching' -and $saved.fixture -eq $true) 'Wrong matching report.'
    }
    Check-IssueCase 'changed build writes a report and still fails the check' {
        $feed = Get-Content -LiteralPath $feedFile -Raw | ConvertFrom-Json
        $feed.builds[0].version = '20990101.1'
        $feed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $feedFile -Encoding UTF8
        $failure = ''
        try { $null = & $checker -Manifest $manifestFile -ChangelogPath $feedFile -ReportPath $reportFile }
        catch { $failure = $_.Exception.Message }
        $saved = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
        Assert-Issue ($failure.Contains('[review-required]') -and $saved.status -eq 'review-required') 'Mismatch was not reported.'
    }
    Check-IssueCase 'a fetch error clears the old mismatch report' {
        & {
            function Invoke-RestMethod { throw 'fixture unavailable changelog' }
            $failure = ''
            try { $null = & $checker -Manifest $manifestFile -ReportPath $reportFile }
            catch { $failure = $_.Exception.Message }
            Assert-Issue ($failure.Contains('[fetch-failed]') -and -not (Test-Path -LiteralPath $reportFile)) 'Stale mismatch report survived a fetch failure.'
        }
    }
    Check-IssueCase 'matching and unknown baselines make no API requests' {
        foreach ($status in @('matching', 'baseline-unverified')) {
            Save-IssueReport $status
            $null = Invoke-IssueReporter
        }
        Assert-Issue ($requests.Count -eq 0) 'An unchanged or unknown baseline contacted GitHub.'
    }
    Check-IssueCase 'fixtures cannot publish issues' {
        Save-IssueReport -Fixture $true
        $failure = ''
        try { $null = Invoke-IssueReporter } catch { $failure = $_.Exception.Message }
        Assert-Issue ($failure.Contains('fixtures cannot create') -and $requests.Count -eq 0) 'Fixture publication was not blocked.'
    }
    Check-IssueCase 'dry run renders a complete issue without network access' {
        $null = & $reporter -ReportPath $reportFile -Repository 'fixture/project' -DryRun -OutputPath $previewFile
        $preview = Get-Content -LiteralPath $previewFile -Raw
        Assert-Issue ($preview.Contains('/docs/stable-profile.json') -and $preview.Contains($baseline.osu_build) -and $preview.Contains('20990101.1')) 'Preview lost version data or the profile link.'
        Assert-Issue ($preview.Contains('Fixture preview') -and $preview.Contains('- [ ] Check reading') -and $preview.Contains('/t')) 'Preview format is incomplete.'
        Assert-Issue ($preview.Length -lt 1000) 'Update issue is no longer concise.'
        Assert-Issue ($requests.Count -eq 0) 'Preview contacted GitHub.'
    }
    Check-IssueCase 'new version creates one issue with the expected payload' {
        Save-IssueReport
        $null = Invoke-IssueReporter
        Assert-Issue ($requests.Count -eq 2 -and $requests[1].method -eq 'Post') 'Expected one lookup and one creation.'
        $payload = [Text.Encoding]::UTF8.GetString($requests[1].body) | ConvertFrom-Json
        Assert-Issue ($payload.title -ceq 'osu!stable 20990101.1: integration review required') 'Unexpected issue title.'
        Assert-Issue ($payload.body.Contains('<!-- osu-chatter:stable-update:20990101.1 -->') -and $payload.body.Contains('/docs/stable-profile.json')) 'Issue lost its marker or profile link.'
    }
    foreach ($state in @('open', 'closed')) {
        $requests.Clear()
        $issuePages = @{ 1 = @([pscustomobject]@{ state = $state; body = '<!-- osu-chatter:stable-update:20990101.1 -->'; html_url = 'https://github.com/fixture/project/issues/1' }) }
        Check-IssueCase "existing $state issue prevents duplicates" {
            $null = Invoke-IssueReporter
            Assert-Issue ($requests.Count -eq 1 -and $requests[0].method -eq 'Get') 'Existing issue was duplicated or modified.'
        }
    }
    Check-IssueCase 'duplicate lookup follows pagination' {
        $requests.Clear()
        $script:issuePages = @{ 1 = @(1..100 | ForEach-Object { [pscustomobject]@{ body = 'Unrelated issue' } }); 2 = @([pscustomobject]@{ body = '<!-- osu-chatter:stable-update:20990101.1 -->'; html_url = 'https://github.com/fixture/project/issues/1' }) }
        $null = Invoke-IssueReporter
        Assert-Issue ($requests.Count -eq 2 -and $requests[1].uri.EndsWith('page=2') -and $requests[1].method -eq 'Get') 'Issue pagination failed.'
    }
    Check-IssueCase 'a matching pull request does not suppress the issue' {
        $requests.Clear()
        $script:issuePages = @{ 1 = @([pscustomobject]@{ body = '<!-- osu-chatter:stable-update:20990101.1 -->'; pull_request = @{ url = 'fixture' } }) }
        $null = Invoke-IssueReporter
        Assert-Issue ($requests.Count -eq 2 -and $requests[1].method -eq 'Post') 'Pull request was treated as an update issue.'
    }
    Check-IssueCase 'another version gets its own issue' {
        $requests.Clear()
        $script:issuePages = @{ 1 = @([pscustomobject]@{ body = '<!-- osu-chatter:stable-update:20980101.1 -->' }) }
        $null = Invoke-IssueReporter
        Assert-Issue ($requests.Count -eq 2 -and $requests[1].method -eq 'Post') 'A different version suppressed the new issue.'
    }
    foreach ($method in @('Get', 'Post')) {
        $requests.Clear(); $issuePages = @{}; $apiFailure = $method
        Check-IssueCase "$method failures propagate without automatic write retries" {
            $failure = ''
            try { $null = Invoke-IssueReporter } catch { $failure = $_.Exception.Message }
            Assert-Issue ($failure.Contains("fixture $method failure")) 'API failure was hidden.'
            $writes = @($requests | Where-Object { $_.method -eq 'Post' }).Count
            Assert-Issue ($writes -eq $(if ($method -eq 'Post') { 1 } else { 0 })) 'Unexpected write or retry after API failure.'
        }
    }
    Write-Output "$issueChecks offline update issue checks passed; GitHub was mocked and no issue was published."
} finally {
    Remove-Item -LiteralPath $manifestFile, $feedFile, $reportFile, $previewFile -Force -ErrorAction SilentlyContinue
}
