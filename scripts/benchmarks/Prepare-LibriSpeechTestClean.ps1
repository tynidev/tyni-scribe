param(
    [int]$Count = 1000,
    [int]$Seed = 20260613,
    [string]$DatasetRoot = (Join-Path $env:LOCALAPPDATA 'tts\datasets\librispeech-test-clean'),
    [string]$DownloadRoot = (Join-Path $env:LOCALAPPDATA 'tts\datasets\downloads'),
    [switch]$ForceDownload,
    [switch]$ForceExtract,
    [switch]$ForceConvert
)

$ErrorActionPreference = 'Stop'

$sourceUrl = 'https://openslr.trmal.net/resources/12/test-clean.tar.gz'
$archivePath = Join-Path $DownloadRoot 'librispeech-test-clean.tar.gz'
$rawRoot = Join-Path $DatasetRoot 'raw'
$wavRoot = Join-Path $DatasetRoot 'wav-16k-mono-pcm16'
$manifestJsonPath = Join-Path $DatasetRoot 'manifest.json'
$manifestCsvPath = Join-Path $DatasetRoot 'manifest.csv'

function Require-Command {
    param([string]$Name, [string]$InstallHint)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

function Get-RelativePath {
    param([string]$BasePath, [string]$Path)

    return [System.IO.Path]::GetRelativePath((Resolve-Path $BasePath), (Resolve-Path $Path))
}

function Get-WavInfo {
    param([string]$Path)

    $json = & ffprobe -v error -select_streams a:0 -show_entries stream=sample_rate,channels,bits_per_sample -show_entries format=duration -of json $Path
    if ($LASTEXITCODE -ne 0) {
        throw "ffprobe failed for $Path"
    }

    $info = $json | ConvertFrom-Json
    $stream = @($info.streams)[0]
    [pscustomobject]@{
        DurationSeconds = [double]$info.format.duration
        SampleRate = [int]$stream.sample_rate
        Channels = [int]$stream.channels
        BitsPerSample = if ($stream.bits_per_sample) { [int]$stream.bits_per_sample } else { 16 }
    }
}

Require-Command ffmpeg 'Install FFmpeg and make sure ffmpeg.exe is on PATH.'
Require-Command ffprobe 'Install FFmpeg and make sure ffprobe.exe is on PATH.'
Require-Command tar 'Install bsdtar/tar or use a recent Windows version with tar.exe on PATH.'

New-Item -ItemType Directory -Force -Path $DatasetRoot, $DownloadRoot, $rawRoot, $wavRoot | Out-Null

if ($ForceDownload -or -not (Test-Path $archivePath)) {
    Write-Host "Downloading LibriSpeech test-clean from $sourceUrl"
    Invoke-WebRequest -Uri $sourceUrl -OutFile $archivePath
}

if ($ForceExtract -or -not (Test-Path (Join-Path $rawRoot 'LibriSpeech\test-clean'))) {
    Write-Host "Extracting $archivePath"
    if ($ForceExtract -and (Test-Path $rawRoot)) {
        Remove-Item $rawRoot -Recurse -Force
        New-Item -ItemType Directory -Force -Path $rawRoot | Out-Null
    }

    & tar -xzf $archivePath -C $rawRoot
    if ($LASTEXITCODE -ne 0) {
        throw 'tar extraction failed.'
    }
}

$testCleanRoot = Join-Path $rawRoot 'LibriSpeech\test-clean'
if (-not (Test-Path $testCleanRoot)) {
    throw "Expected LibriSpeech test-clean folder was not found at $testCleanRoot"
}

Write-Host 'Reading transcripts.'
$utterances = foreach ($transcriptFile in Get-ChildItem -Path $testCleanRoot -Filter '*.trans.txt' -Recurse | Sort-Object FullName) {
    foreach ($line in Get-Content $transcriptFile) {
        $separatorIndex = $line.IndexOf(' ')
        if ($separatorIndex -le 0) {
            continue
        }

        $id = $line.Substring(0, $separatorIndex)
        $text = $line.Substring($separatorIndex + 1).Trim()
        $parts = $id.Split('-')
        if ($parts.Count -lt 3) {
            continue
        }

        $flacPath = Join-Path $transcriptFile.DirectoryName "$id.flac"
        if (-not (Test-Path $flacPath)) {
            throw "Missing FLAC file for transcript id $id"
        }

        [pscustomobject]@{
            Id = $id
            SpeakerId = $parts[0]
            ChapterId = $parts[1]
            FlacPath = $flacPath
            Transcript = $text
        }
    }
}

if ($utterances.Count -lt $Count) {
    throw "Only found $($utterances.Count) utterances, but Count is $Count."
}

$random = [System.Random]::new($Seed)
$selected = $utterances | Sort-Object { $random.Next() } | Select-Object -First $Count

Write-Host "Converting $($selected.Count) files to 16 kHz mono PCM WAV."
$rows = New-Object System.Collections.Generic.List[object]
$index = 0
foreach ($utterance in $selected) {
    $index++
    $speakerDirectory = Join-Path $wavRoot $utterance.SpeakerId
    $chapterDirectory = Join-Path $speakerDirectory $utterance.ChapterId
    New-Item -ItemType Directory -Force -Path $chapterDirectory | Out-Null

    $wavPath = Join-Path $chapterDirectory "$($utterance.Id).wav"
    if ($ForceConvert -or -not (Test-Path $wavPath)) {
        & ffmpeg -y -hide_banner -loglevel error -i $utterance.FlacPath -ar 16000 -ac 1 -c:a pcm_s16le $wavPath
        if ($LASTEXITCODE -ne 0) {
            throw "ffmpeg conversion failed for $($utterance.FlacPath)"
        }
    }

    $wavInfo = Get-WavInfo $wavPath
    $row = [pscustomobject]@{
        id = $utterance.Id
        speakerId = $utterance.SpeakerId
        chapterId = $utterance.ChapterId
        sourceFlacPath = Get-RelativePath $DatasetRoot $utterance.FlacPath
        wavPath = Get-RelativePath $DatasetRoot $wavPath
        transcript = $utterance.Transcript
        durationSeconds = [Math]::Round($wavInfo.DurationSeconds, 6)
        sampleRate = $wavInfo.SampleRate
        channels = $wavInfo.Channels
        bitsPerSample = $wavInfo.BitsPerSample
    }
    $rows.Add($row)

    if ($index % 50 -eq 0) {
        Write-Host "Converted $index / $($selected.Count)"
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    datasetName = 'librispeech-test-clean'
    sourceUrl = $sourceUrl
    datasetRoot = $DatasetRoot
    seed = $Seed
    selectedCount = $rows.Count
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    audioFiles = $rows
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path $manifestJsonPath -Encoding UTF8
$rows | Export-Csv -Path $manifestCsvPath -NoTypeInformation

Write-Host "Wrote $manifestJsonPath"
Write-Host "Wrote $manifestCsvPath"