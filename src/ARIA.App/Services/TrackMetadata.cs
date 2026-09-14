namespace Aria.App.Services;

using System.Text;

public static class TrackMetadata
{
    public static string? ReadDisplayName(string filePath)
    {
        try
        {
            var artistTitle = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".mp3" => ReadMp3(filePath),
                ".flac" => ReadFlac(filePath),
                ".ogg" or ".oga" or ".opus" => ReadOgg(filePath),
                ".wav" => ReadWavInfo(filePath),
                _ => null,
            };
            if (artistTitle is null)
            {
                return null;
            }
            var (artist, title) = artistTitle.Value;
            if (artist.Length > 0 && title.Length > 0)
            {
                return $"{artist} – {title}";
            }
            if (title.Length > 0)
            {
                return title;
            }
            return artist.Length > 0 ? artist : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string Artist, string Title)? ReadMp3(string path)
    {
        using var stream = File.OpenRead(path);
        var fromV2 = ReadId3V2(stream);
        if (fromV2 is not null)
        {
            return fromV2;
        }
        return ReadId3V1(stream);
    }

    private static (string Artist, string Title)? ReadId3V2(FileStream stream)
    {
        var header = new byte[10];
        if (stream.Read(header, 0, 10) != 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
        {
            return null;
        }
        if ((header[5] & 0x40) != 0)
        {
            return null;
        }
        var size = SyncSafe(header, 6);
        var end = stream.Position + size;
        string? artist = null;
        string? title = null;
        var body = new byte[size];
        if (stream.Read(body, 0, size) != size)
        {
            return null;
        }
        if ((header[5] & 0x80) != 0)
        {
            body = TextCodec.StripUnsync(body);
        }
        var syncSafeSizes = header[3] >= 4;
        var offset = 0;
        while (offset + 10 <= body.Length && stream.Position - size + offset < end)
        {
            if (!TryParseId3v2FrameHeader(body, offset, syncSafeSizes, out var id, out var frameSize))
            {
                break;
            }
            DecodeTpe1Tit2Frame(body, offset + 10, id, frameSize, ref artist, ref title);
            offset += 10 + frameSize;
        }
        if (artist is null && title is null)
        {
            return null;
        }
        return (artist ?? string.Empty, title ?? string.Empty);
    }

    private static bool TryParseId3v2FrameHeader(byte[] body, int offset, bool syncSafeSizes, out string id, out int frameSize)
    {
        id = Encoding.Latin1.GetString(body, offset, 4);
        frameSize = 0;
        if (body[offset] == 0)
        {
            return false;
        }
        frameSize = syncSafeSizes ? SyncSafe(body, offset + 4) : ReadBig32(body, offset + 4);
        return frameSize > 1 && offset + 10 + frameSize <= body.Length;
    }

    private static void DecodeTpe1Tit2Frame(byte[] body, int offset, string id, int frameSize, ref string? artist, ref string? title)
    {
        if (id is not ("TPE1" or "TIT2"))
        {
            return;
        }
        var text = TextCodec.DecodeMp3Text(body, offset, frameSize);
        if (text.Length == 0)
        {
            return;
        }
        if (id == "TPE1")
        {
            artist ??= text;
        }
        else
        {
            title ??= text;
        }
    }

    private static (string Artist, string Title)? ReadId3V1(FileStream stream)
    {
        if (stream.Length < 128)
        {
            return null;
        }
        stream.Seek(-128, SeekOrigin.End);
        var tag = new byte[128];
        if (stream.Read(tag, 0, 128) != 128 || tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G')
        {
            return null;
        }
        var title = TextCodec.TrimLatin1(tag, 3, 30);
        var artist = TextCodec.TrimLatin1(tag, 33, 30);
        if (title.Length == 0 && artist.Length == 0)
        {
            return null;
        }
        return (artist, title);
    }

    private static (string Artist, string Title)? ReadFlac(string path)
    {
        using var stream = File.OpenRead(path);
        var marker = new byte[4];
        if (stream.Read(marker, 0, 4) != 4 || marker[0] != 'f' || marker[1] != 'L' || marker[2] != 'a' || marker[3] != 'C')
        {
            return null;
        }
        while (true)
        {
            var head = new byte[4];
            if (stream.Read(head, 0, 4) != 4)
            {
                return null;
            }
            var last = (head[0] & 0x80) != 0;
            var type = head[0] & 0x7F;
            var length = (head[1] << 16) | (head[2] << 8) | head[3];
            if (type == 4)
            {
                var block = new byte[length];
                if (stream.Read(block, 0, length) != length)
                {
                    return null;
                }
                return IsVorbisComment(block) ? TryParseVorbisBlock(block, 7) : null;
            }
            if (stream.Seek(length, SeekOrigin.Current) < 0)
            {
                return null;
            }
            if (last)
            {
                return null;
            }
        }
    }

    private static (string Artist, string Title)? ReadOgg(string path)
    {
        using var stream = File.OpenRead(path);
        var packets = ReadOggPackets(stream);
        if (packets is null)
        {
            return null;
        }
        foreach (var candidate in packets)
        {
            if (IsTaggedPacket(candidate))
            {
                return ParseOpusOrVorbisPacket(candidate);
            }
        }
        return null;
    }

    private static List<byte[]>? ReadOggPackets(FileStream stream)
    {
        var packet = new List<byte>();
        var packets = new List<byte[]>();
        var header = new byte[27];
        while (packets.Count < 2)
        {
            if (stream.Read(header, 0, 27) != 27)
            {
                return null;
            }
            if (header[0] != 'O' || header[1] != 'g' || header[2] != 'g' || header[3] != 'S')
            {
                return null;
            }
            var segments = header[26];
            var table = new byte[segments];
            if (segments > 0 && stream.Read(table, 0, segments) != segments)
            {
                return null;
            }
            foreach (var size in table)
            {
                var chunk = new byte[size];
                var read = 0;
                while (read < size)
                {
                    var got = stream.Read(chunk, read, size - read);
                    if (got == 0)
                    {
                        return null;
                    }
                    read += got;
                }
                packet.AddRange(chunk);
                if (size < 255)
                {
                    packets.Add([.. packet]);
                    packet.Clear();
                }
            }
        }
        return packets;
    }

    private static bool IsVorbisComment(byte[] block) =>
        block.Length > 7 && block[0] == 0x03 && block[1] == 'v'
        && block[2] == 'o' && block[3] == 'r' && block[4] == 'b'
        && block[5] == 'i' && block[6] == 's';

    private static bool IsOpusTags(byte[] block) =>
        block.Length > 8 && block[0] == 'O' && block[1] == 'p'
        && block[2] == 'u' && block[3] == 's' && block[4] == 'T'
        && block[5] == 'a' && block[6] == 'g' && block[7] == 's';

    private static bool IsTaggedPacket(byte[] candidate) => IsVorbisComment(candidate) || IsOpusTags(candidate);

    private static (string Artist, string Title)? ParseOpusOrVorbisPacket(byte[] candidate) =>
        TryParseVorbisBlock(candidate, IsVorbisComment(candidate) ? 7 : 8);

    private static (string Artist, string Title)? TryParseVorbisBlock(byte[] block, int headerBytes)
    {
        var payload = new byte[block.Length - headerBytes];
        Array.Copy(block, headerBytes, payload, 0, payload.Length);
        return ParseVorbisComment(payload);
    }

    private static (string Artist, string Title)? ReadWavInfo(string path)
    {
        using var stream = File.OpenRead(path);
        var riff = new byte[12];
        if (stream.Read(riff, 0, 12) != 12 || riff[0] != 'R' || riff[8] != 'W')
        {
            return null;
        }
        string? artist = null;
        string? title = null;
        var chunk = new byte[8];
        while (stream.Read(chunk, 0, 8) == 8)
        {
            var size = BitConverter.ToInt32(chunk, 4);
            if (size < 0 || size > stream.Length - stream.Position)
            {
                return null;
            }
            var fourcc = Encoding.Latin1.GetString(chunk, 0, 4);
            if (fourcc == "LIST")
            {
                var info = ReadListInfoChunk(stream, size, ref artist, ref title);
                if (info is null)
                {
                    return null;
                }
                if (info.Value)
                {
                    break;
                }
            }
            else
            {
                stream.Seek(size + (size % 2), SeekOrigin.Current);
            }
        }
        if (artist is null && title is null)
        {
            return null;
        }
        return (artist ?? string.Empty, title ?? string.Empty);
    }

    private static bool? ReadListInfoChunk(FileStream stream, int size, ref string? artist, ref string? title)
    {
        var kind = new byte[4];
        if (stream.Read(kind, 0, 4) != 4)
        {
            return null;
        }
        if (Encoding.Latin1.GetString(kind, 0, 4) != "INFO")
        {
            stream.Seek(size - 4, SeekOrigin.Current);
            return false;
        }
        var left = size - 4;
        while (left >= 8)
        {
            if (!ReadInfoSubchunk(stream, ref left, ref artist, ref title))
            {
                return null;
            }
        }
        return true;
    }

    private static bool ReadInfoSubchunk(FileStream stream, ref int left, ref string? artist, ref string? title)
    {
        var sub = new byte[8];
        if (stream.Read(sub, 0, 8) != 8)
        {
            return false;
        }
        var subSize = BitConverter.ToInt32(sub, 4);
        if (subSize < 0 || subSize > left - 8)
        {
            return false;
        }
        var key = Encoding.Latin1.GetString(sub, 0, 4);
        var value = new byte[subSize];
        if (stream.Read(value, 0, subSize) != subSize)
        {
            return false;
        }
        if (subSize % 2 == 1)
        {
            stream.Seek(1, SeekOrigin.Current);
            left -= 1;
        }
        var text = TextCodec.TrimLatin1(value, 0, value.Length);
        if (text.Length > 0)
        {
            if (key == "IART")
            {
                artist ??= text;
            }
            else if (key == "INAM")
            {
                title ??= text;
            }
        }
        left -= 8 + subSize;
        return true;
    }

    private static (string Artist, string Title)? ParseVorbisComment(byte[] block)
    {
        var offset = 0;
        if (!TryRead32(block, ref offset, out var vendor) || vendor < 0 || vendor > block.Length - offset)
        {
            return null;
        }
        offset += vendor;
        if (!TryRead32(block, ref offset, out var count) || count < 0 || count > 512)
        {
            return null;
        }
        string? artist = null;
        string? title = null;
        for (var i = 0; i < count; i++)
        {
            if (!TryRead32(block, ref offset, out var length) || length < 0 || length > block.Length - offset)
            {
                return null;
            }
            var entry = Encoding.UTF8.GetString(block, offset, length);
            offset += length;
            var separator = entry.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }
            var key = entry[..separator].ToUpperInvariant();
            var value = entry[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                continue;
            }
            if (key == "ARTIST")
            {
                artist ??= value;
            }
            else if (key == "TITLE")
            {
                title ??= value;
            }
        }
        if (artist is null && title is null)
        {
            return null;
        }
        return (artist ?? string.Empty, title ?? string.Empty);
    }

    private static bool TryRead32(byte[] block, ref int offset, out int value)
    {
        if (offset + 4 > block.Length)
        {
            value = 0;
            return false;
        }
        value = BitConverter.ToInt32(block, offset);
        offset += 4;
        return true;
    }

    private static int SyncSafe(byte[] buffer, int offset) =>
        ((buffer[offset] & 0x7F) << 21) | ((buffer[offset + 1] & 0x7F) << 14)
        | ((buffer[offset + 2] & 0x7F) << 7) | (buffer[offset + 3] & 0x7F);

    private static int ReadBig32(byte[] buffer, int offset) =>
        (buffer[offset] << 24) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 8) | buffer[offset + 3];

}
