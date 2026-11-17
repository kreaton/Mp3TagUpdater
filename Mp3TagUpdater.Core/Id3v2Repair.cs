namespace Mp3TagUpdater.Services;

/// <summary>The repaired file contents, and a description of each change made.</summary>
public sealed record Id3v2RepairResult(byte[] Data, IReadOnlyList<string> Actions);

/// <summary>
/// Repairs ID3v2 tag faults that stop TagLib from opening an otherwise good mp3.
/// Only the tag region is changed: the file keeps its size and the audio bytes
/// are never moved.
/// </summary>
public static class Id3v2Repair
{
    private const int HeaderSize = 10;

    // TagLib refuses APIC/GEOB frames smaller than this, and at that size they
    // cannot hold any actual picture or object data.
    private const int MinAttachmentFrameSize = 5;

    private readonly record struct Frame(int Offset, int TotalSize, string Id, long DataSize)
    {
        public bool IsEmptyAttachment =>
            Id is "APIC" or "GEOB" && DataSize < MinAttachmentFrameSize;
    }

    /// <summary>
    /// Returns a repaired copy of <paramref name="data"/>, or null if the file has
    /// no fault this class recognises or its tag can't be parsed with confidence.
    /// </summary>
    public static Id3v2RepairResult? TryRepair(byte[] data)
    {
        if (data.Length < HeaderSize || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
            return null;

        // Unsynchronised tags, extended headers and footers change the layout
        // parsed below. None of the known faults involve them, so leave them alone.
        if ((data[5] & 0xD0) != 0)
            return null;

        if (data[6] >= 0x80 || data[7] >= 0x80 || data[8] >= 0x80 || data[9] >= 0x80)
            return null;

        long tagEnd = HeaderSize + ReadSyncSafe(data, 6);
        if (tagEnd > data.Length)
            return null;

        // A bad version byte such as 0x23 is only corrected to its low nibble if
        // the whole frame table then parses cleanly under that version.
        int version = data[3] is 3 or 4 ? data[3] : data[3] & 0x0F;
        if (version is not (3 or 4))
            return null;

        var frames = ParseFrames(data, version, tagEnd, out int framesEnd);
        if (frames is null || frames.Count == 0)
            return null;

        var repaired = (byte[])data.Clone();
        var actions = new List<string>();

        if (data[3] != version)
        {
            repaired[3] = (byte)version;
            actions.Add($"Corrected ID3v2 version byte 0x{data[3]:X2} to 0x{version:X2}");
        }

        RemoveEmptyAttachments(data, repaired, frames, framesEnd, actions);
        CorrectOversizedTag(data, repaired, framesEnd, tagEnd, actions);

        return actions.Count == 0 ? null : new Id3v2RepairResult(repaired, actions);
    }

    private static List<Frame>? ParseFrames(byte[] data, int version, long tagEnd, out int framesEnd)
    {
        var frames = new List<Frame>();
        int offset = HeaderSize;

        while (offset + HeaderSize <= tagEnd && data[offset] != 0)
        {
            var idBytes = data.AsSpan(offset, 4);
            foreach (var b in idBytes)
            {
                if (b is not (>= (byte)'A' and <= (byte)'Z' or >= (byte)'0' and <= (byte)'9'))
                {
                    framesEnd = offset;
                    return null;
                }
            }

            long size = version == 3 ? ReadBigEndian(data, offset + 4) : ReadSyncSafe(data, offset + 4);
            if (offset + HeaderSize + size > tagEnd)
            {
                framesEnd = offset;
                return null;
            }

            int total = HeaderSize + (int)size;
            frames.Add(new Frame(offset, total, System.Text.Encoding.ASCII.GetString(idBytes), size));
            offset += total;
        }

        framesEnd = offset;
        return frames;
    }

    /// <summary>
    /// Closes the gaps left by removed frames and turns the freed space at the
    /// end of the frame table into padding, so the tag stays the same size.
    /// </summary>
    private static void RemoveEmptyAttachments(
        byte[] data, byte[] repaired, List<Frame> frames, int framesEnd, List<string> actions)
    {
        if (!frames.Any(f => f.IsEmptyAttachment))
            return;

        int writeAt = HeaderSize;
        foreach (var frame in frames)
        {
            if (frame.IsEmptyAttachment)
            {
                actions.Add($"Removed empty {frame.Id} frame ({frame.DataSize} bytes)");
                continue;
            }

            Array.Copy(data, frame.Offset, repaired, writeAt, frame.TotalSize);
            writeAt += frame.TotalSize;
        }

        Array.Clear(repaired, writeAt, framesEnd - writeAt);
    }

    /// <summary>
    /// If the tag's stated size runs past its padding and into an MPEG frame,
    /// shrinks the stated size so that the audio is no longer treated as tag.
    /// </summary>
    private static void CorrectOversizedTag(
        byte[] data, byte[] repaired, int framesEnd, long tagEnd, List<string> actions)
    {
        int audioStart = framesEnd;
        while (audioStart < tagEnd && data[audioStart] == 0)
            audioStart++;

        if (audioStart >= tagEnd - 1 || data[audioStart] != 0xFF || (data[audioStart + 1] & 0xE0) != 0xE0)
            return;

        WriteSyncSafe(repaired, 6, audioStart - HeaderSize);
        actions.Add($"Corrected tag size, which overlapped the audio by {tagEnd - audioStart} bytes");
    }

    private static long ReadSyncSafe(byte[] b, int o) =>
        ((long)b[o] << 21) | ((long)b[o + 1] << 14) | ((long)b[o + 2] << 7) | b[o + 3];

    private static long ReadBigEndian(byte[] b, int o) =>
        ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];

    private static void WriteSyncSafe(byte[] b, int o, int value)
    {
        b[o] = (byte)((value >> 21) & 0x7F);
        b[o + 1] = (byte)((value >> 14) & 0x7F);
        b[o + 2] = (byte)((value >> 7) & 0x7F);
        b[o + 3] = (byte)(value & 0x7F);
    }
}
