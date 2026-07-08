<#
.SYNOPSIS
Processes and summarizes yt-channel videos in alternating batches.

.DESCRIPTION
Runs `yt-channel process` for a channel, then immediately runs `yt-channel summarize`
for the ready transcripts from that same channel. By default the script refuses to
start if the channel already has completed transcripts waiting for summaries,
because that would make it ambiguous whether the summarize step is summarizing the
same videos that were just processed.

.EXAMPLE
./scripts/Invoke-YtChannelBatchPipeline.ps1 -Channel UCIaH-gZIVC432YRjNVvnyCA

.EXAMPLE
./scripts/Invoke-YtChannelBatchPipeline.ps1 -Channel UCIaH-gZIVC432YRjNVvnyCA -BatchSize 10 -MaxCycles 5

.EXAMPLE
./scripts/Invoke-YtChannelBatchPipeline.ps1 -Channel UCIaH-gZIVC432YRjNVvnyCA -UntilEmpty -MaxCycles 0
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Channel,

    [int]$BatchSize = 10,

    [int]$MaxCycles = 1,

    [switch]$UntilEmpty,

    [switch]$IncludeShorts,

    [switch]$ForceAudio,

    [string]$Language = 'en',

    [string]$Provider,

    [string]$OutputDirectory,

    [string]$Configuration = 'Release',

    [string]$DatabasePath = (Join-Path $env:APPDATA 'yt-channel\channel-data.db'),

    [switch]$AllowExistingReadySummaries
)

$ErrorActionPreference = 'Stop'

if ($BatchSize -le 0) {
    throw '-BatchSize must be greater than zero.'
}

if ($MaxCycles -lt 0) {
    throw '-MaxCycles must be zero or greater. Use zero with -UntilEmpty for no cycle limit.'
}

function Resolve-SqliteCommand {
    $command = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $commonPath = 'C:\Program Files\SQLite\sqlite3.exe'
    if (Test-Path -LiteralPath $commonPath) {
        return $commonPath
    }

    throw 'sqlite3 was not found. Install SQLite or add sqlite3.exe to PATH.'
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

function Invoke-SqliteScalar {
    param([string]$Query)

    $result = & $script:Sqlite $script:DatabasePath $Query
    if ($LASTEXITCODE -ne 0) {
        throw "sqlite3 query failed with exit code $LASTEXITCODE."
    }

    return ($result | Select-Object -First 1)
}

function Resolve-ChannelId {
    param([string]$InputValue)

    $escaped = Escape-SqlLiteral $InputValue
    $query = "SELECT ChannelId FROM Channels WHERE ChannelId = '$escaped' OR ChannelUrl = '$escaped' OR ChannelName = '$escaped' LIMIT 1;"
    $channelId = Invoke-SqliteScalar $query
    if ([string]::IsNullOrWhiteSpace($channelId)) {
        throw "Channel '$InputValue' was not found in the yt-channel database. Sync it first or pass an existing ChannelId/ChannelUrl/ChannelName."
    }

    return $channelId.Trim()
}

function Get-ReadyProcessCount {
    param([string]$ChannelId)

    $shortsPredicate = if ($IncludeShorts) { '' } else { "AND NOT (v.DurationSeconds IS NOT NULL AND v.DurationSeconds <= 180 AND v.IsShortsPlaylistVideo = 1)" }
    $escaped = Escape-SqlLiteral $ChannelId
    $query = @"
SELECT COUNT(*)
FROM Videos v
INNER JOIN Channels c ON c.ChannelId = v.ChannelId
WHERE v.ChannelId = '$escaped'
  AND v.TranscriptStatus = 'pending'
  AND (v.DurationSeconds IS NULL OR v.DurationSeconds > 0)
  AND (c.MaxVideoAgeDays IS NULL OR v.PublishedAt IS NULL OR julianday(v.PublishedAt) >= julianday('now') - c.MaxVideoAgeDays)
  $shortsPredicate;
"@

    return [int](Invoke-SqliteScalar $query)
}

function Get-ReadySummaryCount {
    param([string]$ChannelId)

    $shortsPredicate = if ($IncludeShorts) { '' } else { "AND NOT (v.DurationSeconds IS NOT NULL AND v.DurationSeconds <= 180 AND v.IsShortsPlaylistVideo = 1)" }
    $escaped = Escape-SqlLiteral $ChannelId
    $query = @"
SELECT COUNT(*)
FROM Videos v
INNER JOIN Channels c ON c.ChannelId = v.ChannelId
WHERE v.ChannelId = '$escaped'
  AND v.TranscriptStatus = 'completed'
  AND v.SummaryStatus = 'pending'
  AND (c.MaxVideoAgeDays IS NULL OR v.PublishedAt IS NULL OR julianday(v.PublishedAt) >= julianday('now') - c.MaxVideoAgeDays)
  $shortsPredicate;
"@

    return [int](Invoke-SqliteScalar $query)
}

function Invoke-YtChannel {
    param([string[]]$Arguments)

    $projectPath = Join-Path $script:RepoRoot 'src\YtChannel.Cli\YtChannel.Cli.csproj'
    $dotnetArgs = @('run', '--project', $projectPath, '-c', $Configuration, '--') + $Arguments
    Write-Host ''
    Write-Host "> dotnet $($dotnetArgs -join ' ')"
    & dotnet @dotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "yt-channel command failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $DatabasePath)) {
    throw "Database not found: $DatabasePath"
}

$script:Sqlite = Resolve-SqliteCommand
$script:DatabasePath = $DatabasePath
$script:RepoRoot = Split-Path -Parent $PSScriptRoot
$channelId = Resolve-ChannelId $Channel

Write-Host "Channel      : $Channel"
Write-Host "ChannelId    : $channelId"
Write-Host "Batch size   : $BatchSize"
Write-Host "Max cycles   : $MaxCycles"
Write-Host "Until empty  : $UntilEmpty"
Write-Host "Database     : $DatabasePath"

$cycle = 0
while ($true) {
    if (-not $UntilEmpty -and $cycle -ge $MaxCycles) {
        break
    }

    if ($UntilEmpty -and $MaxCycles -gt 0 -and $cycle -ge $MaxCycles) {
        break
    }

    $cycle++
    $readyToProcess = Get-ReadyProcessCount $channelId
    $readySummariesBefore = Get-ReadySummaryCount $channelId

    Write-Host ''
    Write-Host ('=' * 80)
    Write-Host "Cycle $cycle"
    Write-Host "Ready to process before cycle : $readyToProcess"
    Write-Host "Ready to summarize before cycle: $readySummariesBefore"

    if ($readyToProcess -le 0) {
        Write-Host 'No videos are ready to process. Stopping.'
        break
    }

    if ($readySummariesBefore -gt 0 -and -not $AllowExistingReadySummaries) {
        throw "Channel already has $readySummariesBefore completed transcript(s) waiting for summaries. Run summarize first, or pass -AllowExistingReadySummaries if exact same-batch pairing is not required."
    }

    $processArgs = @('process', $Channel, '--max-videos', $BatchSize.ToString(), '--language', $Language)
    if ($IncludeShorts) { $processArgs += '--include-shorts' }
    if ($ForceAudio) { $processArgs += '--force-audio' }
    if (-not [string]::IsNullOrWhiteSpace($Provider)) { $processArgs += @('--provider', $Provider) }
    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory)) { $processArgs += @('--output-dir', $OutputDirectory) }
    Invoke-YtChannel $processArgs

    $readySummariesAfter = Get-ReadySummaryCount $channelId
    $summariesToRun = [Math]::Min($BatchSize, $readySummariesAfter)
    Write-Host ''
    Write-Host "Ready to summarize after process: $readySummariesAfter"

    if ($summariesToRun -le 0) {
        Write-Host 'No newly processed transcripts are ready for summaries. Stopping.'
        break
    }

    $summaryArgs = @('summarize', $Channel, '--max-videos', $summariesToRun.ToString())
    if ($IncludeShorts) { $summaryArgs += '--include-shorts' }
    Invoke-YtChannel $summaryArgs
}

Write-Host ''
Write-Host 'Batch pipeline complete.'
Write-Host "Remaining ready to process : $(Get-ReadyProcessCount $channelId)"
Write-Host "Remaining ready to summarize: $(Get-ReadySummaryCount $channelId)"