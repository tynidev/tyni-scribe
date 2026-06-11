using NAudio.Wave;

namespace Tts.App.Services.Audio;

public sealed record AudioCaptureFormat(
    int SampleRate,
    int Channels,
    int BitsPerSample,
    string Encoding)
{
    public static AudioCaptureFormat FromWaveFormat(WaveFormat waveFormat)
    {
        return new AudioCaptureFormat(
            waveFormat.SampleRate,
            waveFormat.Channels,
            waveFormat.BitsPerSample,
            waveFormat.Encoding.ToString());
    }
}