param(
	[string]$ChannelUrl = 'https://www.youtube.com/@ChrisWillx',
	[int]$Method2SampleSize = 300
)

$ErrorActionPreference = 'Stop'

Set-Location (Join-Path $PSScriptRoot '..\..')

if (-not $env:YOUTUBE_API_KEY -or [string]::IsNullOrWhiteSpace($env:YOUTUBE_API_KEY)) {
	if (Test-Path '.env') {
		Get-Content '.env' | ForEach-Object {
			$line = $_.Trim()
			if (-not $line -or $line.StartsWith('#')) {
				return
			}

			$parts = $line -split '=', 2
			if ($parts.Length -ne 2) {
				return
			}

			$name = $parts[0].Trim()
			$value = $parts[1].Trim().Trim('"')
			if ($name -eq 'YOUTUBE_API_KEY' -and -not [string]::IsNullOrWhiteSpace($value)) {
				$env:YOUTUBE_API_KEY = $value
			}
		}
	}
}

if (-not $env:YOUTUBE_API_KEY -or [string]::IsNullOrWhiteSpace($env:YOUTUBE_API_KEY)) {
	throw 'YOUTUBE_API_KEY is not set in environment or .env'
}

$dbPath = Join-Path $env:APPDATA 'yt-channel\channel-data.db'
if (-not (Test-Path $dbPath)) {
	throw "Database not found: $dbPath"
}

$channelId = (sqlite3 $dbPath "select ChannelId from Channels where ChannelUrl = '$ChannelUrl' limit 1;").Trim()
if (-not $channelId) {
	throw "Channel not found in db: $ChannelUrl"
}

$rowsRaw = sqlite3 -separator "`t" $dbPath "select VideoId, coalesce(cast(DurationSeconds as text), ''), coalesce(PublishedAt, '') from Videos where ChannelId = '$channelId' order by PublishedAt desc;"
$rows = @()
foreach ($line in $rowsRaw) {
	$parts = [string]$line -split "`t", 3
	if ($parts.Length -lt 1 -or [string]::IsNullOrWhiteSpace($parts[0])) {
		continue
	}

	$durationValue = $null
	if ($parts.Length -ge 2 -and -not [string]::IsNullOrWhiteSpace($parts[1])) {
		$durationValue = [double]$parts[1]
	}

	$publishedAt = if ($parts.Length -ge 3) { $parts[2] } else { '' }
	$rows += [pscustomobject]@{
		VideoId = $parts[0]
		DurationSeconds = $durationValue
		PublishedAt = $publishedAt
	}
}

if (-not $rows -or $rows.Count -eq 0) {
	throw "No videos found for channel id $channelId"
}

$allVideoIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$durationShortIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$durationSixtySecondIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($row in $rows) {
	$id = [string]$row.VideoId
	[void]$allVideoIds.Add($id)
	if ($null -ne $row.DurationSeconds -and [double]$row.DurationSeconds -le 60) {
		[void]$durationSixtySecondIds.Add($id)
	}
	if ($null -ne $row.DurationSeconds -and [double]$row.DurationSeconds -le 180) {
		[void]$durationShortIds.Add($id)
	}
}

# Method 1: UUSH playlist heuristic
$method1Ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$uushPlaylistId = 'UUSH' + $channelId.Substring(2)
$pageToken = $null
$apiCalls = 0
while ($true) {
	$queryParams = @{
		part = 'contentDetails'
		playlistId = $uushPlaylistId
		maxResults = '50'
		key = $env:YOUTUBE_API_KEY
	}
	if ($pageToken) {
		$queryParams.pageToken = $pageToken
	}

	$queryString = ($queryParams.GetEnumerator() | ForEach-Object {
		'{0}={1}' -f [Uri]::EscapeDataString($_.Key), [Uri]::EscapeDataString([string]$_.Value)
	}) -join '&'

	$url = "https://www.googleapis.com/youtube/v3/playlistItems?$queryString"
	$response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 30
	$apiCalls++

	foreach ($item in $response.items) {
		$videoId = [string]$item.contentDetails.videoId
		if ($allVideoIds.Contains($videoId)) {
			[void]$method1Ids.Add($videoId)
		}
	}

	$pageToken = [string]$response.nextPageToken
	if (-not $pageToken) {
		break
	}
}

# Method 2: /shorts/<id> redirect behavior (sample latest N videos)
$sampleSize = [Math]::Min([Math]::Max($Method2SampleSize, 1), $rows.Count)
$sampleRows = $rows | Select-Object -First $sampleSize
$method2Ids = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$method2Errors = 0
$geminiHeadIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$geminiHeadStatusCounts = @{}
$geminiHeadErrors = 0
$httpHandler = [System.Net.Http.HttpClientHandler]::new()
$httpHandler.AllowAutoRedirect = $false
$httpClient = [System.Net.Http.HttpClient]::new($httpHandler)
$httpClient.Timeout = [TimeSpan]::FromSeconds(20)
$httpClient.DefaultRequestHeaders.UserAgent.ParseAdd('Mozilla/5.0 (Windows NT 10.0; Win64; x64)')

foreach ($row in $sampleRows) {
	$id = [string]$row.VideoId
	$url = "https://www.youtube.com/shorts/$id"

	try {
		$request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Head, $url)
		$response = $httpClient.Send($request)
		$statusCode = [int]$response.StatusCode
		$statusName = [string]$response.StatusCode
		$statusKey = "$statusCode $statusName"
		if (-not $geminiHeadStatusCounts.ContainsKey($statusKey)) {
			$geminiHeadStatusCounts[$statusKey] = 0
		}
		$geminiHeadStatusCounts[$statusKey]++

		if ($statusCode -eq 200) {
			[void]$geminiHeadIds.Add($id)
		}

		$response.Dispose()
		$request.Dispose()
	}
	catch {
		$geminiHeadErrors++
	}

	try {
		$resp = Invoke-WebRequest -Uri $url -Method Get -MaximumRedirection 5 -TimeoutSec 20 -Headers @{
			'User-Agent' = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
		}

		$finalUrl = [string]$resp.BaseResponse.ResponseUri.AbsoluteUri
		if ($finalUrl -match '/shorts/') {
			[void]$method2Ids.Add($id)
		}
	}
	catch {
		$method2Errors++
	}
}

$httpClient.Dispose()
$httpHandler.Dispose()

function Get-SetOverlapCount {
	param(
		[System.Collections.Generic.HashSet[string]]$First,
		[System.Collections.Generic.HashSet[string]]$Second
	)

	$count = 0
	foreach ($id in $First) {
		if ($Second.Contains($id)) {
			$count++
		}
	}

	return $count
}

$durationCount = $durationShortIds.Count
$durationSixtyCount = $durationSixtySecondIds.Count
$method1Count = $method1Ids.Count
$m1VsDurationOverlap = Get-SetOverlapCount -First $method1Ids -Second $durationShortIds
$m1Only = $method1Count - $m1VsDurationOverlap
$durationOnlyVsM1 = $durationCount - $m1VsDurationOverlap
$m1VsDurationSixtyOverlap = Get-SetOverlapCount -First $method1Ids -Second $durationSixtySecondIds
$m1OnlyVsDurationSixty = $method1Count - $m1VsDurationSixtyOverlap
$durationSixtyOnlyVsM1 = $durationSixtyCount - $m1VsDurationSixtyOverlap

$sampleDurationSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sampleDurationSixtySet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$sampleMethod1Set = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($row in $sampleRows) {
	$id = [string]$row.VideoId
	if ($durationSixtySecondIds.Contains($id)) {
		[void]$sampleDurationSixtySet.Add($id)
	}
	if ($durationShortIds.Contains($id)) {
		[void]$sampleDurationSet.Add($id)
	}
	if ($method1Ids.Contains($id)) {
		[void]$sampleMethod1Set.Add($id)
	}
}

$method2Count = $method2Ids.Count
$m2VsDurationOverlap = Get-SetOverlapCount -First $method2Ids -Second $sampleDurationSet
$m2OnlyVsDuration = $method2Count - $m2VsDurationOverlap
$durationOnlyVsM2 = $sampleDurationSet.Count - $m2VsDurationOverlap

$m2VsM1Overlap = Get-SetOverlapCount -First $method2Ids -Second $sampleMethod1Set
$m2OnlyVsM1 = $method2Count - $m2VsM1Overlap
$m1OnlyVsM2 = $sampleMethod1Set.Count - $m2VsM1Overlap

$geminiHeadCount = $geminiHeadIds.Count
$geminiHeadVsDurationOverlap = Get-SetOverlapCount -First $geminiHeadIds -Second $sampleDurationSet
$geminiHeadOnlyVsDuration = $geminiHeadCount - $geminiHeadVsDurationOverlap
$durationOnlyVsGeminiHead = $sampleDurationSet.Count - $geminiHeadVsDurationOverlap
$geminiHeadVsDurationSixtyOverlap = Get-SetOverlapCount -First $geminiHeadIds -Second $sampleDurationSixtySet
$geminiHeadOnlyVsDurationSixty = $geminiHeadCount - $geminiHeadVsDurationSixtyOverlap
$durationSixtyOnlyVsGeminiHead = $sampleDurationSixtySet.Count - $geminiHeadVsDurationSixtyOverlap
$geminiHeadVsM1Overlap = Get-SetOverlapCount -First $geminiHeadIds -Second $sampleMethod1Set
$geminiHeadOnlyVsM1 = $geminiHeadCount - $geminiHeadVsM1Overlap
$m1OnlyVsGeminiHead = $sampleMethod1Set.Count - $geminiHeadVsM1Overlap

Write-Host "=== Shorts Method Comparison ==="
Write-Host "Channel URL: $ChannelUrl"
Write-Host "Channel ID: $channelId"
Write-Host "DB videos: $($rows.Count)"
Write-Host "Duration<=60 count (all videos): $durationSixtyCount"
Write-Host "Duration<=180 count (all videos): $durationCount"
Write-Host "Method1 UUSH count (all videos): $method1Count"
Write-Host "Method1 ∩ Duration<=60: $m1VsDurationSixtyOverlap"
Write-Host "Method1 only (not <=60): $m1OnlyVsDurationSixty"
Write-Host "Duration<=60 only (not method1): $durationSixtyOnlyVsM1"
Write-Host "Method1 ∩ Duration<=180: $m1VsDurationOverlap"
Write-Host "Method1 only (not <=180): $m1Only"
Write-Host "Duration<=180 only (not method1): $durationOnlyVsM1"
Write-Host ""
Write-Host "Method2 sample size: $sampleSize"
Write-Host "Gemini HEAD 200/303 errors: $geminiHeadErrors"
Write-Host "Gemini HEAD status counts:"
foreach ($statusKey in ($geminiHeadStatusCounts.Keys | Sort-Object)) {
	Write-Host "  ${statusKey}: $($geminiHeadStatusCounts[$statusKey])"
}
Write-Host "Gemini HEAD 200 count (sample): $geminiHeadCount"
Write-Host "Gemini HEAD 200 ∩ Duration<=60(sample): $geminiHeadVsDurationSixtyOverlap"
Write-Host "Gemini HEAD 200 only vs Duration<=60(sample): $geminiHeadOnlyVsDurationSixty"
Write-Host "Duration<=60 only vs Gemini HEAD 200(sample): $durationSixtyOnlyVsGeminiHead"
Write-Host "Gemini HEAD 200 ∩ Duration<=180(sample): $geminiHeadVsDurationOverlap"
Write-Host "Gemini HEAD 200 only vs Duration<=180(sample): $geminiHeadOnlyVsDuration"
Write-Host "Duration<=180 only vs Gemini HEAD 200(sample): $durationOnlyVsGeminiHead"
Write-Host "Gemini HEAD 200 ∩ Method1(sample): $geminiHeadVsM1Overlap"
Write-Host "Gemini HEAD 200 only vs Method1(sample): $geminiHeadOnlyVsM1"
Write-Host "Method1 only vs Gemini HEAD 200(sample): $m1OnlyVsGeminiHead"
Write-Host ""
Write-Host "Method2 errors: $method2Errors"
Write-Host "Method2 count (sample): $method2Count"
Write-Host "Method2 ∩ Duration<=180(sample): $m2VsDurationOverlap"
Write-Host "Method2 only vs Duration<=180(sample): $m2OnlyVsDuration"
Write-Host "Duration<=180 only vs Method2(sample): $durationOnlyVsM2"
Write-Host "Method2 ∩ Method1(sample): $m2VsM1Overlap"
Write-Host "Method2 only vs Method1(sample): $m2OnlyVsM1"
Write-Host "Method1 only vs Method2(sample): $m1OnlyVsM2"
