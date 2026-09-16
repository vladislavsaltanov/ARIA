namespace Aria.Audio;

using Aria.Core.Playback;

public interface ISourceFactory
{
    ISampleSource? Open(string filePath, TimeSpan cueIn, TimeSpan? cueOut);

    bool TryOpen(string filePath, TimeSpan cueIn, TimeSpan? cueOut, out ISampleSource? source, out SourceOpenFault fault)
    {
        source = Open(filePath, cueIn, cueOut);
        fault = source is null ? SourceOpenFault.Undecodable : SourceOpenFault.Unknown;
        return source is not null;
    }
}
