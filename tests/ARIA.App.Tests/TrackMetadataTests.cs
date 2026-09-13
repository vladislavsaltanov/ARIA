namespace Aria.App.Tests;

using System.Text;
using Aria.App.Services;
using Aria.Audio;

public sealed class TrackMetadataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aria-meta-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Mp3_Id3V2_ReadsArtistAndTitle()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "song.mp3");
        WriteMp3(path, "Осенний дождь", "Ария кавер");
        File.WriteAllBytes(path, [.. File.ReadAllBytes(path), .. new byte[1024]]);

        Assert.Equal("Ария кавер – Осенний дождь", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void Mp3_Id3V2_Utf16_KeepsTrailingParen()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "utf16.mp3");
        WriteMp3Utf16(path, "Song (Remix)", "Band");
        File.WriteAllBytes(path, [.. File.ReadAllBytes(path), .. new byte[1024]]);

        Assert.Equal("Band – Song (Remix)", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void Mp3_Id3V1_FallsBack_WhenNoV2()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "oldie.mp3");
        using (var stream = File.Create(path))
        {
            stream.Write(new byte[512]);
            var tag = new byte[128];
            tag[0] = (byte)'T';
            tag[1] = (byte)'A';
            tag[2] = (byte)'G';
            Encoding.Latin1.GetBytes("Old Title").CopyTo(tag, 3);
            Encoding.Latin1.GetBytes("Old Artist").CopyTo(tag, 33);
            stream.Write(tag);
        }

        Assert.Equal("Old Artist – Old Title", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void Flac_VorbisComment_ReadsArtistAndTitle()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "track.flac");
        WriteFlac(path, "Путь", "Группа");

        Assert.Equal("Группа – Путь", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void Ogg_OpusTags_ReadsTitleOnly()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "voice.opus");
        WriteOgg(path, null, "Голос");

        Assert.Equal("Голос", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void Wav_InfoChunk_ReadsArtistAndTitle()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteTaggedWav("meta.wav", "Rain", "Aria");

        Assert.Equal("Aria – Rain", TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void PlainWav_ReturnsNull()
    {
        Directory.CreateDirectory(_directory);
        var path = TestWav.Write(_directory, "plain.wav");

        Assert.Null(TrackMetadata.ReadDisplayName(path));
    }

    [Fact]
    public void TrackImporter_PrefersMetadataName()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteTaggedWav("band - song.wav", "Song", "Band");
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));

        var imported = importer.Import(path);

        Assert.NotNull(imported);
        Assert.Equal("Band – Song", imported.Track.DefaultName);
    }

    [Fact]
    public void TrackImporter_FallsBackToFileName()
    {
        Directory.CreateDirectory(_directory);
        var path = TestWav.Write(_directory, "demo-track.wav");
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));

        var imported = importer.Import(path);

        Assert.NotNull(imported);
        Assert.Equal("demo-track", imported.Track.DefaultName);
    }

    [Fact]
    public void RefreshDisplayName_RepairsStaleFileName()
    {
        Directory.CreateDirectory(_directory);
        var path = WriteTaggedWav("stale-name.wav", "Song", "Band");
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));
        var stale = imported_Stale(path);

        var repaired = importer.RefreshDisplayName(stale);

        Assert.Equal("Band – Song", repaired.DefaultName);
    }

    [Fact]
    public void RefreshDisplayName_KeepsName_WhenNoTags()
    {
        Directory.CreateDirectory(_directory);
        var path = TestWav.Write(_directory, "plain.wav");
        var factory = new MiniaudioSourceFactory(8000, 1);
        var importer = new TrackImporter(factory, new WaveformScanner(factory));
        var track = new Aria.Core.Model.Track(Aria.Core.Model.TrackId.New(), path, "plain", TimeSpan.FromSeconds(1), new Aria.Core.Model.TrackDefaults());

        Assert.Equal(track, importer.RefreshDisplayName(track));
    }

    private static Aria.Core.Model.Track imported_Stale(string path) =>
        new(Aria.Core.Model.TrackId.New(), path, Path.GetFileNameWithoutExtension(path), TimeSpan.FromSeconds(1), new Aria.Core.Model.TrackDefaults());

    private static void WriteMp3(string path, string title, string artist)
    {
        using var stream = File.Create(path);
        var frames = new List<byte>();
        WriteTextFrame(frames, "TIT2", title);
        WriteTextFrame(frames, "TPE1", artist);
        stream.Write("ID3"u8);
        stream.Write([(byte)3, (byte)0, (byte)0]);
        var size = frames.Count;
        stream.Write([(byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)]);
        stream.Write(frames.ToArray());
    }

    private static void WriteTextFrame(List<byte> output, string id, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        output.AddRange(Encoding.Latin1.GetBytes(id));
        var size = payload.Length + 1;
        output.AddRange([(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size]);
        output.AddRange([(byte)0, (byte)0, (byte)3]);
        output.AddRange(payload);
    }

    private static void WriteMp3Utf16(string path, string title, string artist)
    {
        using var stream = File.Create(path);
        var frames = new List<byte>();
        WriteTextFrame16(frames, "TIT2", title);
        WriteTextFrame16(frames, "TPE1", artist);
        stream.Write("ID3"u8);
        stream.Write([(byte)3, (byte)0, (byte)0]);
        var size = frames.Count;
        stream.Write([(byte)((size >> 21) & 0x7F), (byte)((size >> 14) & 0x7F), (byte)((size >> 7) & 0x7F), (byte)(size & 0x7F)]);
        stream.Write(frames.ToArray());
    }

    private static void WriteTextFrame16(List<byte> output, string id, string text)
    {
        var payload = new List<byte> { 1, 0xFF, 0xFE };
        payload.AddRange(Encoding.Unicode.GetBytes(text));
        payload.AddRange([(byte)0, (byte)0]);
        output.AddRange(Encoding.Latin1.GetBytes(id));
        var size = payload.Count;
        output.AddRange([(byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size]);
        output.AddRange([(byte)0, (byte)0]);
        output.AddRange(payload);
    }

    private static void WriteFlac(string path, string title, string artist)
    {
        using var stream = File.Create(path);
        stream.Write("fLaC"u8);
        var comment = BuildVorbisComment(title, artist);
        stream.Write([(byte)0x84, (byte)(comment.Length >> 16), (byte)(comment.Length >> 8), (byte)comment.Length]);
        stream.Write(comment);
    }

    private static void WriteOgg(string path, string? artist, string? title)
    {
        using var stream = File.Create(path);
        var tags = BuildVorbisComment(title, artist, opus: true);
        var filler = new byte[] { 0x04, 0x05, 0x06, 0x07, 0x08 };
        stream.Write(BuildOggPage(2, [tags, filler]));
    }

    private static byte[] BuildOggPage(int serial, byte[][] packets)
    {
        var page = new List<byte>();
        page.AddRange("OggS"u8);
        page.Add(0);
        page.Add(2);
        page.AddRange(new byte[8]);
        page.AddRange(BitConverter.GetBytes(serial));
        page.AddRange(BitConverter.GetBytes(0));
        page.AddRange(BitConverter.GetBytes(0));
        page.Add((byte)packets.Length);
        foreach (var packet in packets)
        {
            page.Add((byte)packet.Length);
        }
        foreach (var packet in packets)
        {
            page.AddRange(packet);
        }
        return [.. page];
    }

    private static byte[] BuildVorbisComment(string? title, string? artist, bool opus = false)
    {
        var output = new List<byte>();
        output.AddRange(opus ? "OpusTags"u8 : [0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s']);
        var vendor = Encoding.UTF8.GetBytes("ARIA-test");
        output.AddRange(BitConverter.GetBytes(vendor.Length));
        output.AddRange(vendor);
        var entries = new List<string>();
        if (artist is not null)
        {
            entries.Add($"ARTIST={artist}");
        }
        if (title is not null)
        {
            entries.Add($"TITLE={title}");
        }
        output.AddRange(BitConverter.GetBytes(entries.Count));
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            output.AddRange(BitConverter.GetBytes(bytes.Length));
            output.AddRange(bytes);
        }
        return [.. output];
    }

    private string WriteTaggedWav(string fileName, string title, string artist)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, fileName);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(8000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        var info = BuildInfoChunk(title, artist);
        writer.Write(info);
        var frames = 8000;
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var value = Math.Sin(2.0 * Math.PI * 440.0 * i / 8000);
            writer.Write((short)(value * short.MaxValue));
        }
        writer.Flush();
        var size = (int)stream.Length - 8;
        stream.Seek(4, SeekOrigin.Begin);
        writer.Write(size);
        return path;
    }

    private static byte[] BuildInfoChunk(string title, string artist)
    {
        var output = new List<byte>();
        output.AddRange("LIST"u8);
        var body = new List<byte>();
        body.AddRange("INFO"u8);
        WriteInfoValue(body, "INAM", title);
        WriteInfoValue(body, "IART", artist);
        output.AddRange(BitConverter.GetBytes(body.Count));
        output.AddRange(body);
        return [.. output];
    }

    private static void WriteInfoValue(List<byte> output, string key, string value)
    {
        output.AddRange(Encoding.Latin1.GetBytes(key));
        var bytes = Encoding.Latin1.GetBytes(value);
        output.AddRange(BitConverter.GetBytes(bytes.Length + 1));
        output.AddRange(bytes);
        output.Add(0);
        if (bytes.Length % 2 == 0)
        {
            output.Add(0);
        }
    }
}
