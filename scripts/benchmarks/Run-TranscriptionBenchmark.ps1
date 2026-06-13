param(
    [string]$ManifestPath = (Join-Path $env:LOCALAPPDATA 'tts\datasets\librispeech-test-clean\manifest.json'),
    [string]$CliPath = (Join-Path (Get-Location) 'src\Tts.Cli\bin\Release\net8.0-windows\Tts.Cli.exe'),
    [string]$Provider,
    [string]$Model,
    [string]$Language = 'en',
    [int]$TimeoutSeconds = 600,
    [string[]]$Setting = @(),
    [string]$MatrixPath,
    [int]$Count = 1000,
    [int]$WarmupCount = 1,
    [int]$PerFileTimeoutSeconds = 120,
    [string]$OutputDirectory = (Join-Path $env:APPDATA 'SpeechToTextDaemon\logs\benchmarks'),
    [switch]$BuildCli,
    [switch]$KeepTranscripts,
    [switch]$ColdPerFile
)

$ErrorActionPreference = 'Stop'

function ConvertTo-SettingDictionary {
    param([string[]]$Pairs)

    $settings = [ordered]@{}
    foreach ($pair in $Pairs) {
        $separatorIndex = $pair.IndexOf('=')
        if ($separatorIndex -le 0 -or $separatorIndex -eq $pair.Length - 1) {
            throw "Settings must use key=value syntax: $pair"
        }

        $settings[$pair.Substring(0, $separatorIndex).Trim()] = $pair.Substring($separatorIndex + 1).Trim()
    }

    return $settings
}

function Get-Runs {
    if ($MatrixPath) {
        $matrix = Get-Content $MatrixPath -Raw | ConvertFrom-Json
        $matrixRuns = if ($matrix.runs) { @($matrix.runs) } else { @($matrix) }
        foreach ($run in $matrixRuns) {
            [pscustomobject]@{
                label = if ($run.label) { $run.label } else { $run.provider }
                provider = $run.provider
                model = $run.model
                language = if ($run.language) { $run.language } else { 'en' }
                timeoutSeconds = if ($run.timeoutSeconds) { [int]$run.timeoutSeconds } else { 600 }
                settings = if ($run.settings) { $run.settings } else { [pscustomobject]@{} }
            }
        }

        return
    }

    if (-not $Provider) {
        throw 'Specify -Provider or -MatrixPath.'
    }

    [pscustomobject]@{
        label = $Provider
        provider = $Provider
        model = $Model
        language = $Language
        timeoutSeconds = $TimeoutSeconds
        settings = ConvertTo-SettingDictionary $Setting
    }
}

function Add-OptionalArg {
    param([System.Collections.Generic.List[string]]$Arguments, [string]$Name, [object]$Value)

    if ($null -ne $Value -and -not [string]::IsNullOrWhiteSpace([string]$Value)) {
        $Arguments.Add($Name)
        $Arguments.Add([string]$Value)
    }
}

function Get-NormalizedWords {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $normalized = $Text.ToLowerInvariant() -replace "[^a-z0-9']+", ' '
    return @($normalized -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Get-WordErrorStats {
    param([string]$Expected, [string]$Actual)

    $reference = @(Get-NormalizedWords $Expected)
    $hypothesis = @(Get-NormalizedWords $Actual)
    $referenceCount = $reference.Count
    $hypothesisCount = $hypothesis.Count

    if ($referenceCount -eq 0) {
        $distance = $hypothesisCount
    }
    else {
        $previous = New-Object int[] ($hypothesisCount + 1)
        $current = New-Object int[] ($hypothesisCount + 1)

        for ($column = 0; $column -le $hypothesisCount; $column++) {
            $previous[$column] = $column
        }

        for ($row = 1; $row -le $referenceCount; $row++) {
            $current[0] = $row
            for ($column = 1; $column -le $hypothesisCount; $column++) {
                $cost = if ($reference[$row - 1] -eq $hypothesis[$column - 1]) { 0 } else { 1 }
                $deleteCost = $previous[$column] + 1
                $insertCost = $current[$column - 1] + 1
                $substituteCost = $previous[$column - 1] + $cost
                $current[$column] = [Math]::Min([Math]::Min($deleteCost, $insertCost), $substituteCost)
            }

            $swap = $previous
            $previous = $current
            $current = $swap
        }

        $distance = $previous[$hypothesisCount]
    }

    $wordErrorRate = if ($referenceCount -gt 0) { [Math]::Round($distance / [double]$referenceCount, 6) } else { $null }
    $wordAccuracy = if ($null -ne $wordErrorRate) { [Math]::Round([Math]::Max(0.0, [double](1.0 - $wordErrorRate)), 6) } else { $null }

    [pscustomobject]@{
        referenceWordCount = $referenceCount
        hypothesisWordCount = $hypothesisCount
        wordErrors = $distance
        wordErrorRate = $wordErrorRate
        wordAccuracy = $wordAccuracy
        exactNormalizedMatch = (($reference -join ' ') -eq ($hypothesis -join ' '))
    }
}

function Invoke-CliTranscription {
    param(
        [string]$AudioPath,
        [object]$Run,
        [string]$MetricsPath,
        [string]$TranscriptPath,
        [string]$ErrorPath
    )

    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add('transcribe')
    $arguments.Add('--audio')
    $arguments.Add($AudioPath)
    Add-OptionalArg $arguments '--provider' $Run.provider
    Add-OptionalArg $arguments '--model' $Run.model
    Add-OptionalArg $arguments '--language' $Run.language
    Add-OptionalArg $arguments '--timeout-seconds' $Run.timeoutSeconds
    $arguments.Add('--metrics-output')
    $arguments.Add($MetricsPath)

    if ($Run.settings -is [System.Collections.IDictionary]) {
        foreach ($key in $Run.settings.Keys) {
            if ($null -eq $Run.settings[$key] -or [string]::IsNullOrWhiteSpace([string]$Run.settings[$key])) {
                continue
            }

            $arguments.Add('--setting')
            $arguments.Add("$key=$($Run.settings[$key])")
        }
    }
    else {
        foreach ($property in $Run.settings.PSObject.Properties) {
            if ($null -eq $property.Value -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                continue
            }

            $arguments.Add('--setting')
            $arguments.Add("$($property.Name)=$($property.Value)")
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = $CliPath
    foreach ($argument in $arguments) {
        [void]$process.StartInfo.ArgumentList.Add($argument)
    }

    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $completed = $process.WaitForExit($PerFileTimeoutSeconds * 1000)
    if (-not $completed) {
        try {
            $process.Kill($true)
        }
        catch {
        }

        $process.WaitForExit()
    }

    $stopwatch.Stop()

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Set-Content -Path $TranscriptPath -Value $stdout -NoNewline
    Set-Content -Path $ErrorPath -Value $stderr -NoNewline

    [pscustomobject]@{
        exitCode = if ($completed) { $process.ExitCode } else { -1 }
        elapsedMilliseconds = [long][Math]::Round($stopwatch.Elapsed.TotalMilliseconds, [MidpointRounding]::AwayFromZero)
        stderr = if ($completed) { $stderr } else { 'benchmark-process-timeout' }
    }
}

function Invoke-CliBatchTranscription {
    param(
        [string]$ManifestPath,
        [object]$Run,
        [int]$Count,
        [string]$CsvPath,
        [string]$JsonPath
    )

    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add('transcribe-batch')
    $arguments.Add('--manifest')
    $arguments.Add($ManifestPath)
    Add-OptionalArg $arguments '--provider' $Run.provider
    Add-OptionalArg $arguments '--model' $Run.model
    Add-OptionalArg $arguments '--language' $Run.language
    Add-OptionalArg $arguments '--timeout-seconds' $Run.timeoutSeconds
    $arguments.Add('--count')
    $arguments.Add([string]$Count)
    $arguments.Add('--warmup-first-file')
    $arguments.Add('--output-csv')
    $arguments.Add($CsvPath)
    $arguments.Add('--output-json')
    $arguments.Add($JsonPath)

    if ($Run.settings -is [System.Collections.IDictionary]) {
        foreach ($key in $Run.settings.Keys) {
            if ($null -eq $Run.settings[$key] -or [string]::IsNullOrWhiteSpace([string]$Run.settings[$key])) {
                continue
            }

            $arguments.Add('--setting')
            $arguments.Add("$key=$($Run.settings[$key])")
        }
    }
    else {
        foreach ($property in $Run.settings.PSObject.Properties) {
            if ($null -eq $property.Value -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                continue
            }

            $arguments.Add('--setting')
            $arguments.Add("$($property.Name)=$($property.Value)")
        }
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo.FileName = $CliPath
    foreach ($argument in $arguments) {
        [void]$process.StartInfo.ArgumentList.Add($argument)
    }

    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true

    [void]$process.Start()
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($stdout) {
        Write-Host $stdout
    }
    if ($stderr) {
        Write-Host $stderr
    }

    if ($process.ExitCode -ne 0) {
        Write-Warning "Batch transcription reported failures for $($Run.label) with exit code $($process.ExitCode)."
    }

    return $process.ExitCode
}

if ($BuildCli -or -not (Test-Path $CliPath)) {
    dotnet build src/Tts.Cli/Tts.Cli.csproj -c Release
    if ($LASTEXITCODE -ne 0) {
        throw 'CLI build failed.'
    }
}

if (-not (Test-Path $CliPath)) {
    throw "Tts.Cli.exe was not found at $CliPath"
}

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest was not found at $ManifestPath"
}

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$datasetRoot = $manifest.datasetRoot
$files = @($manifest.audioFiles) | Select-Object -First $Count
$runs = @(Get-Runs)
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resultCsvPath = Join-Path $OutputDirectory "transcription-benchmark-$runId.csv"
$resultJsonPath = Join-Path $OutputDirectory "transcription-benchmark-$runId.json"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "tts-benchmark-$runId"
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

$results = New-Object System.Collections.Generic.List[object]

if (-not $ColdPerFile) {
    $allRows = New-Object System.Collections.Generic.List[object]
    try {
        foreach ($run in $runs) {
            Write-Host "Benchmarking $($run.label) over $($files.Count) files using batch mode."
            $runCsvPath = Join-Path $tempRoot "$($run.label)-batch.csv"
            $runJsonPath = Join-Path $tempRoot "$($run.label)-batch.json"
            $batchExitCode = Invoke-CliBatchTranscription -ManifestPath $ManifestPath -Run $run -Count $Count -CsvPath $runCsvPath -JsonPath $runJsonPath
            if ($batchExitCode -ne 0 -and -not (Test-Path $runCsvPath)) {
                throw "Batch transcription failed for $($run.label) with exit code $batchExitCode and did not produce a CSV."
            }
            $runRows = Import-Csv $runCsvPath
            foreach ($row in $runRows) {
                $allRows.Add($row)
            }
        }
    }
    finally {
        if (-not $KeepTranscripts) {
            Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    $allRows | Export-Csv -Path $resultCsvPath -NoTypeInformation
    $allRows | ConvertTo-Json -Depth 6 | Set-Content -Path $resultJsonPath -Encoding UTF8
    Write-Host "Wrote $resultCsvPath"
    Write-Host "Wrote $resultJsonPath"
    return
}

try {
    foreach ($run in $runs) {
        Write-Host "Benchmarking $($run.label) over $($files.Count) files."
        $sequence = @()
        if ($WarmupCount -gt 0) {
            $sequence += $files | Select-Object -First $WarmupCount | ForEach-Object { [pscustomobject]@{ file = $_; isWarmup = $true } }
        }
        $sequence += $files | ForEach-Object { [pscustomobject]@{ file = $_; isWarmup = $false } }

        $index = 0
        foreach ($item in $sequence) {
            $file = $item.file
            $index++
            $audioPath = Join-Path $datasetRoot $file.wavPath
            $safeId = $file.id -replace '[^A-Za-z0-9_.-]', '_'
            $metricsPath = Join-Path $tempRoot "$($run.label)-$safeId.metrics.json"
            $transcriptPath = Join-Path $tempRoot "$($run.label)-$safeId.transcript.txt"
            $errorPath = Join-Path $tempRoot "$($run.label)-$safeId.stderr.txt"

            $processResult = Invoke-CliTranscription -AudioPath $audioPath -Run $run -MetricsPath $metricsPath -TranscriptPath $transcriptPath -ErrorPath $errorPath
            $metrics = if (Test-Path $metricsPath) { Get-Content $metricsPath -Raw | ConvertFrom-Json } else { $null }
            $actualTranscript = if (Test-Path $transcriptPath) { Get-Content $transcriptPath -Raw } else { '' }
            $score = Get-WordErrorStats -Expected $file.transcript -Actual $actualTranscript
            $audioDuration = if ($metrics -and $metrics.audioDurationSeconds) { [double]$metrics.audioDurationSeconds } else { [double]$file.durationSeconds }
            $realTimeFactor = if ($audioDuration -gt 0) { [Math]::Round(($processResult.elapsedMilliseconds / 1000.0) / $audioDuration, 6) } else { $null }
            $processAudioSecondsPerSecond = if ($processResult.elapsedMilliseconds -gt 0) { [Math]::Round($audioDuration / ($processResult.elapsedMilliseconds / 1000.0), 6) } else { $null }
            $status = if ($processResult.exitCode -eq 0) { 'success' } else { 'failure' }
            $errorCategory = if ($metrics) { $metrics.errorCategory } else { ($processResult.stderr -split "`r?`n" | Where-Object { $_ } | Select-Object -Last 1) }

            if (-not $item.isWarmup) {
                $row = [pscustomobject]@{
                    runId = $runId
                    runLabel = $run.label
                    fileId = $file.id
                    providerId = $run.provider
                    modelId = $run.model
                    language = $run.language
                    timeoutSeconds = $run.timeoutSeconds
                    audioDurationSeconds = $audioDuration
                    processElapsedMs = $processResult.elapsedMilliseconds
                    audioProcessingMs = if ($metrics) { $metrics.audioProcessingMilliseconds } else { $null }
                    transcriptionMs = if ($metrics) { $metrics.transcriptionMilliseconds } else { $null }
                    transcriptionRealTimeFactor = if ($metrics) { $metrics.transcriptionRealTimeFactor } else { $null }
                    transcriptionAudioSecondsPerSecond = if ($metrics) { $metrics.transcriptionAudioSecondsPerSecond } else { $null }
                    cliTotalMs = if ($metrics) { $metrics.totalMilliseconds } else { $null }
                    processRealTimeFactor = $realTimeFactor
                    processAudioSecondsPerSecond = $processAudioSecondsPerSecond
                    referenceWordCount = $score.referenceWordCount
                    hypothesisWordCount = $score.hypothesisWordCount
                    wordErrors = $score.wordErrors
                    wordErrorRate = $score.wordErrorRate
                    wordAccuracy = $score.wordAccuracy
                    exactNormalizedMatch = $score.exactNormalizedMatch
                    status = $status
                    exitCode = $processResult.exitCode
                    errorCategory = $errorCategory
                }
                $results.Add($row)
                $row | Export-Csv -Path $resultCsvPath -NoTypeInformation -Append
            }

            if (-not $KeepTranscripts) {
                Remove-Item $transcriptPath, $errorPath, $metricsPath -Force -ErrorAction SilentlyContinue
            }

            if ($index % 25 -eq 0) {
                Write-Host "Processed $index / $($sequence.Count) for $($run.label)"
            }
        }
    }
}
finally {
    if (-not $KeepTranscripts) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$results | ConvertTo-Json -Depth 6 | Set-Content -Path $resultJsonPath -Encoding UTF8
Write-Host "Wrote $resultCsvPath"
Write-Host "Wrote $resultJsonPath"