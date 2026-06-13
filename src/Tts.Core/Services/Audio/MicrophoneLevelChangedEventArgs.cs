namespace Tts.Core.Services.Audio;

public sealed class MicrophoneLevelChangedEventArgs : EventArgs
{
    public MicrophoneLevelChangedEventArgs(double level)
    {
        Level = level;
    }

    public double Level { get; }
}