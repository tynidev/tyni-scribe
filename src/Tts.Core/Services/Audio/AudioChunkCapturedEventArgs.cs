namespace Tts.Core.Services.Audio;

public sealed class AudioChunkCapturedEventArgs : EventArgs
{
    public AudioChunkCapturedEventArgs(byte[] audioData, AudioCaptureFormat format)
    {
        AudioData = audioData;
        Format = format;
    }

    public byte[] AudioData { get; }

    public AudioCaptureFormat Format { get; }
}