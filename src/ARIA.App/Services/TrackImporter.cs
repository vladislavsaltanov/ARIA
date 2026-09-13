namespace Aria.App.Services;

using Aria.Audio;
using Aria.Core.Model;

public sealed record ImportedTrack(Track Track, WaveformPeaks? Peaks);

public sealed class TrackImporter
{
    private const int ReadBlockFrames = 4096;

    private readonly MiniaudioSourceFactory _factory;
    private readonly WaveformScanner _scanner;

    public TrackImporter(MiniaudioSourceFactory factory, WaveformScanner scanner)
    {
        _factory = factory;
        _scanner = scanner;
    }

    public ImportedTrack? Import(string filePath, int pointsPerSecond = 25)
    {
        var source = _factory.Open(filePath, TimeSpan.Zero, null);
        if (source is null)
        {
            return null;
        }
        using var scope = (IDisposable)source;

        var totalFrames = 0L;
        var buffer = new float[ReadBlockFrames * source.Channels];
        while (source.ReadFrames(buffer) is var read && read > 0)
        {
            totalFrames += read;
        }
        if (totalFrames == 0)
        {
            return null;
        }

        var duration = TimeSpan.FromSeconds(totalFrames / (double)source.SampleRate);
        var track = new Track(
            TrackId.New(),
            filePath,
            TrackMetadata.ReadDisplayName(filePath) ?? Path.GetFileNameWithoutExtension(filePath),
            duration,
            new TrackDefaults());
        var peaks = _scanner.Scan(filePath, track.Id, pointsPerSecond);
        return new ImportedTrack(track, peaks);
    }

    public Track RefreshDisplayName(Track track)
    {
        if (track.DefaultName != Path.GetFileNameWithoutExtension(track.FilePath))
        {
            return track;
        }
        var name = TrackMetadata.ReadDisplayName(track.FilePath);
        return name is not null && name != track.DefaultName ? track with { DefaultName = name } : track;
    }
}
