namespace Aria.Audio;

public interface ISourceFactory
{
    ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut);
}
