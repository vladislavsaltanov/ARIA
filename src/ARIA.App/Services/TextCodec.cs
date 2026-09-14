namespace Aria.App.Services;

using System.Text;

internal static class TextCodec
{
    internal static string DecodeMp3Text(byte[] buffer, int offset, int length)
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
            if ((count & 1) == 1)
            {
                count--;
            }
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

    internal static string TrimLatin1(byte[] buffer, int offset, int length)
    {
        var end = offset + Math.Min(length, buffer.Length - offset);
        var stop = end;
        while (stop > offset && (buffer[stop - 1] == 0 || buffer[stop - 1] == 0x20))
        {
            stop--;
        }
        return Encoding.Latin1.GetString(buffer, offset, stop - offset).Trim();
    }

    internal static byte[] StripUnsync(byte[] body)
    {
        var output = new List<byte>(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            output.Add(body[i]);
            if (body[i] == 0xFF && i + 1 < body.Length && body[i + 1] == 0x00)
            {
                i++;
            }
        }
        return [.. output];
    }
}
