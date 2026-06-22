param(
    [string]$DatabasePath = (Join-Path $env:APPDATA 'yt-channel\channel-data.db'),
    [int]$IntervalSeconds = 30,
    [switch]$Once
)

$ErrorActionPreference = 'Stop'

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

function ConvertFrom-SqliteTsv {
    param([string[]]$Rows)

    foreach ($row in $Rows) {
        if ([string]::IsNullOrWhiteSpace($row)) {
            continue
        }

        $columns = $row -split "`t", 6
        [pscustomobject]@{
            ChannelId = $columns[0]
            ChannelName = if ($columns[1]) { $columns[1] } else { $columns[0] }
            Discovered = [int]$columns[2]
            Transcribed = [int]$columns[3]
            Summarized = [int]$columns[4]
            FailedSummaries = [int]$columns[5]
        }
    }
}

$sqlite = Resolve-SqliteCommand

$query = @'
.mode tabs
SELECT
    c.ChannelId,
    COALESCE(c.ChannelName, c.ChannelId) AS ChannelName,
    COUNT(v.VideoId) AS Discovered,
    SUM(CASE WHEN v.TranscriptStatus = 'completed' THEN 1 ELSE 0 END) AS Transcribed,
    SUM(CASE WHEN v.SummaryStatus = 'summarized' THEN 1 ELSE 0 END) AS Summarized,
    SUM(CASE WHEN v.SummaryStatus = 'failed' THEN 1 ELSE 0 END) AS FailedSummaries
FROM Channels c
LEFT JOIN Videos v ON v.ChannelId = c.ChannelId
GROUP BY c.ChannelId, c.ChannelName
ORDER BY ChannelName COLLATE NOCASE;
'@

do {
    if (-not (Test-Path -LiteralPath $DatabasePath)) {
        throw "Database not found: $DatabasePath"
    }

    Write-Host ('=' * 80)
    Write-Host "yt-channel progress by channel"
    Write-Host "Database : $DatabasePath"
    Write-Host "Updated  : $((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))"
    Write-Host "Interval : $IntervalSeconds seconds"
    Write-Host ''

    $rows = $query | & $sqlite $DatabasePath
    $items = @(ConvertFrom-SqliteTsv -Rows $rows)
    $items | Format-Table -AutoSize ChannelName, ChannelId, Discovered, Transcribed, Summarized, FailedSummaries

    $totals = [pscustomobject]@{
        ChannelName = 'TOTAL'
        ChannelId = ''
        Discovered = [int](($items | Measure-Object Discovered -Sum).Sum ?? 0)
        Transcribed = [int](($items | Measure-Object Transcribed -Sum).Sum ?? 0)
        Summarized = [int](($items | Measure-Object Summarized -Sum).Sum ?? 0)
        FailedSummaries = [int](($items | Measure-Object FailedSummaries -Sum).Sum ?? 0)
    }
    $totals | Format-Table -AutoSize ChannelName, ChannelId, Discovered, Transcribed, Summarized, FailedSummaries

    if ($Once) {
        break
    }

    Start-Sleep -Seconds $IntervalSeconds
} while ($true)