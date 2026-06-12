using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tts.App.Services.Transcription;

public sealed class WhisperWarmServerEngine : IWhisperWarmEngine
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly HttpClient _httpClient = new();
    private Process? _serverProcess;
    private Uri? _serverUri;
    private string? _loadedModelPath;
    private string? _loadedLanguage;
    private bool _disposed;

    public async Task<string> TranscribeAsync(WhisperWarmTranscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _sync.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            await EnsureServerAsync(request, cancellationToken);
            return await TranscribeWithServerAsync(request, cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopServer();
        _sync.Dispose();
        _httpClient.Dispose();
    }

    private async Task EnsureServerAsync(WhisperWarmTranscriptionRequest request, CancellationToken cancellationToken)
    {
        if (_serverProcess is { HasExited: false }
            && _serverUri is not null
            && _loadedModelPath is not null
            && _loadedModelPath.Equals(request.ModelPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_loadedLanguage, NormalizeLanguage(request.Language), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        StopServer();

        var port = ReserveLocalPort();
        var serverUri = new Uri($"http://127.0.0.1:{port}/");
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ServerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(request.ServerExecutablePath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(request.ModelPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("-nt");

        var language = NormalizeLanguage(request.Language);
        if (!string.IsNullOrWhiteSpace(language))
        {
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(language);
        }

        _serverProcess = new Process { StartInfo = startInfo };
        if (!_serverProcess.Start())
        {
            throw new InvalidOperationException("Could not start the warm local Whisper engine.");
        }

        _ = DrainProcessOutputAsync(_serverProcess.StandardOutput);
        _ = DrainProcessOutputAsync(_serverProcess.StandardError);

        _serverUri = serverUri;
        _loadedModelPath = request.ModelPath;
        _loadedLanguage = language;

        try
        {
            await WaitForServerAsync(serverUri, cancellationToken);
        }
        catch
        {
            StopServer();
            throw;
        }
    }

    private async Task<string> TranscribeWithServerAsync(WhisperWarmTranscriptionRequest request, CancellationToken cancellationToken)
    {
        if (_serverUri is null)
        {
            throw new InvalidOperationException("The warm local Whisper engine is not initialized.");
        }

        await using var audioStream = new FileStream(
            request.AudioFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        using var form = new MultipartFormDataContent();
        using var audioContent = new StreamContent(audioStream);
        form.Add(audioContent, "file", Path.GetFileName(request.AudioFilePath));
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0"), "temperature");

        var language = NormalizeLanguage(request.Language);
        if (!string.IsNullOrWhiteSpace(language))
        {
            form.Add(new StringContent(language), "language");
        }

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsync(new Uri(_serverUri, "inference"), form, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StopServer();
            throw;
        }

        using (response)
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                StopServer();
                throw new InvalidOperationException("Warm local whisper.cpp transcription failed.");
            }

            var result = await JsonSerializer.DeserializeAsync<WhisperServerInferenceResponse>(responseStream, cancellationToken: cancellationToken);
            return result?.Text ?? string.Empty;
        }
    }

    private async Task WaitForServerAsync(Uri serverUri, CancellationToken cancellationToken)
    {
        using var readyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readyCancellation.CancelAfter(TimeSpan.FromSeconds(30));

        while (!readyCancellation.IsCancellationRequested)
        {
            if (_serverProcess?.HasExited == true)
            {
                throw new InvalidOperationException("The warm local Whisper engine exited during startup.");
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, serverUri);
                using var response = await _httpClient.SendAsync(request, readyCancellation.Token);
                return;
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(100, readyCancellation.Token);
        }

        throw new TimeoutException("The warm local Whisper engine did not become ready in time.");
    }

    private void StopServer()
    {
        var process = _serverProcess;
        _serverProcess = null;
        _serverUri = null;
        _loadedModelPath = null;
        _loadedLanguage = null;

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static int ReserveLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string NormalizeLanguage(string language)
    {
        return string.IsNullOrWhiteSpace(language) ? "en" : language.Trim();
    }

    private static async Task DrainProcessOutputAsync(StreamReader reader)
    {
        try
        {
            while (await reader.ReadLineAsync() is not null)
            {
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record WhisperServerInferenceResponse([property: JsonPropertyName("text")] string Text);
}