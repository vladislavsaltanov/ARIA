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
        var syncSafeSizes = header[3] >= 4;
        var offset = 0;
        while (offset + 10 <= body.Length && stream.Position - size + offset < end)
        {
            var id = Encoding.Latin1.GetString(body, offset, 4);
            if (body[offset] == 0)
            {
                break;
            }
            var frameSize = syncSafeSizes ? SyncSafe(body, offset + 4) : ReadBig32(body, offset + 4);
            if (frameSize <= 1 || offset + 10 + frameSize > body.Length)
            {
                break;
            }
            if (id is "TPE1" or "TIT2")
            {
                var text = DecodeText(body, offset + 10, frameSize);
                if (text.Length > 0)
                {
                    if (id == "TPE1")
                    {
                        artist ??= text;
                    }
                    else
                    {
                        title ??= text;
                    }
                }
            }
            offset += 10 + frameSize;
        }
        if (artist is null && title is null)
        {
            return null;
        }
        return (artist ?? string.Empty, title ?? string.Empty);
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
        var title = CleanLatin1(tag, 3, 30);
        var artist = CleanLatin1(tag, 33, 30);
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
                if (length > 7 && block[0] == 0x03 && block[1] == 'v'
                    && block[2] == 'o' && block[3] == 'r' && block[4] == 'b'
                    && block[5] == 'i' && block[6] == 's')
                {
                    var payload = new byte[length - 7];
                    Array.Copy(block, 7, payload, 0, payload.Length);
                    return ParseVorbisComment(payload);
                }
                return null;
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
        foreach (var candidate in packets)
        {
            if (candidate.Length > 7 && candidate[0] == 0x03 && candidate[1] == 'v'
                && candidate[2] == 'o' && candidate[3] == 'r' && candidate[4] == 'b'
                && candidate[5] == 'i' && candidate[6] == 's')
            {
                var payload = new byte[candidate.Length - 7];
                Array.Copy(candidate, 7, payload, 0, payload.Length);
                return ParseVorbisComment(payload);
            }
            if (candidate.Length > 8 && candidate[0] == 'O' && candidate[1] == 'p'
                && candidate[2] == 'u' && candidate[3] == 's' && candidate[4] == 'T'
                && candidate[5] == 'a' && candidate[6] == 'g' && candidate[7] == 's')
            {
                var payload = new byte[candidate.Length - 8];
                Array.Copy(candidate, 8, payload, 0, payload.Length);
                return ParseVorbisComment(payload);
            }
        }
        return null;
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
                var kind = new byte[4];
                if (stream.Read(kind, 0, 4) != 4)
                {
                    return null;
                }
                if (Encoding.Latin1.GetString(kind, 0, 4) == "INFO")
                {
                    var left = size - 4;
                    while (left >= 8)
                    {
                        var sub = new byte[8];
                        if (stream.Read(sub, 0, 8) != 8)
                        {
                            return null;
                        }
                        var subSize = BitConverter.ToInt32(sub, 4);
                        if (subSize < 0 || subSize > left - 8)
                        {
                            return null;
                        }
                        var key = Encoding.Latin1.GetString(sub, 0, 4);
                        var value = new byte[subSize];
                        if (stream.Read(value, 0, subSize) != subSize)
                        {
                            return null;
                        }
                        if (subSize % 2 == 1)
                        {
                            stream.Seek(1, SeekOrigin.Current);
                            left -= 1;
                        }
                        var text = CleanLatin1(value, 0, value.Length);
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
                    }
                    break;
                }
                stream.Seek(size - 4, SeekOrigin.Current);
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

    private static string DecodeText(byte[] buffer, int offset, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }
        var encoding = buffer[offset];
        var start = offset + 1;
        var count = length - 1;
        if (encoding is 1 or 2)
        {
            var bigEndian = encoding == 2;
            var end16 = start + count;
            while (count >= 2)
            {
                var hi = bigEndian ? buffer[end16 - 2] : buffer[end16 - 1];
                var lo = bigEndian ? buffer[end16 - 1] : buffer[end16 - 2];
                if (((hi << 8) | lo) is not (0x0000 or 0x0020))
                {
                    break;
                }
                count -= 2;
                end16 -= 2;
            }
            if (!bigEndian && count >= 2 && buffer[start] == 0xFF && buffer[start + 1] == 0xFE)
            {
                start += 2;
                count -= 2;
            }
            return (bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(buffer, start, count & ~1);
        }
        var end = start + count;
        while (count > 0 && (buffer[end - 1] == 0 || buffer[end - 1] == 0x20))
        {
            count--;
            end--;
        }
        return encoding == 3
            ? Encoding.UTF8.GetString(buffer, start, count)
            : Encoding.Latin1.GetString(buffer, start, count);
    }

    private static string CleanLatin1(byte[] buffer, int offset, int length)
    {
        var end = offset + Math.Min(length, buffer.Length - offset);
        var stop = end;
        while (stop > offset && (buffer[stop - 1] == 0 || buffer[stop - 1] == 0x20))
        {
            stop--;
        }
        return Encoding.Latin1.GetString(buffer, offset, stop - offset).Trim();
    }
}
