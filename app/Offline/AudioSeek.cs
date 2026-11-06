using System.IO;
using System.Text;
namespace WinSqueeze.Offline;

// Seeking inside a downloaded file: (bytes of header to send first, byte offset to carry on from, the exact second that offset starts at)
static class AudioSeek
{
    static byte[] ReadAt(Stream file, long offset, int count) { var bytes = new byte[count]; file.Position = offset; var total = 0; while (total < count) { var read = file.Read(bytes, total, count - total); if (read <= 0) break; total += read; } return total == count ? bytes : bytes[..total]; }
    static long Id3Length(Stream file) { var head = ReadAt(file, 0, 10); return head.Length == 10 && head[0] == 'I' && head[1] == 'D' && head[2] == '3' ? 10 + (head[6] << 21 | head[7] << 14 | head[8] << 7 | head[9]) + ((head[5] & 0x10) != 0 ? 10 : 0) : 0; }

    public static (long, long, double)? Flac(Stream file, OfflineTrack track, double seconds)
    {
        var position = Id3Length(file);
        if (Encoding.ASCII.GetString(ReadAt(file, position, 4)) != "fLaC") return null;
        position += 4;
        int sampleRate = 0, minBlockSize = 0; long totalSamples = 0; var seekPoints = new List<(long sample, long offset)>();
        while (true)
        {
            var blockHeader = ReadAt(file, position, 4);
            if (blockHeader.Length < 4) return null;
            var isLast = (blockHeader[0] & 0x80) != 0; var blockType = blockHeader[0] & 0x7F; var blockLength = blockHeader[1] << 16 | blockHeader[2] << 8 | blockHeader[3];
            if (blockType == 0)
            {
                var info = ReadAt(file, position + 4, 18);
                minBlockSize = info[0] << 8 | info[1];
                sampleRate = info[10] << 12 | info[11] << 4 | info[12] >> 4;
                totalSamples = (long)(info[13] & 0x0F) << 32 | (long)info[14] << 24 | (long)info[15] << 16 | (long)info[16] << 8 | info[17];
            }
            else if (blockType == 3)
            {
                var table = ReadAt(file, position + 4, blockLength);
                for (var point = 0; point + 18 <= table.Length; point += 18)
                {
                    var sample = (long)BitConverter.ToUInt64(table[point..(point + 8)].Reverse().ToArray());
                    if (sample != -1) seekPoints.Add((sample, (long)BitConverter.ToUInt64(table[(point + 8)..(point + 16)].Reverse().ToArray())));
                }
            }
            position += 4 + blockLength;
            if (isLast) break;
        }
        var audioStart = position;
        if (sampleRate == 0) return null;
        var targetSample = (long)(seconds * sampleRate);
        // A seek table gives an exact frame start; otherwise estimate by bytes and find the next real frame header
        var bestPoint = seekPoints.Where(point => point.sample <= targetSample).OrderBy(point => point.sample).LastOrDefault();
        if (seekPoints.Count > 0 && bestPoint.sample > 0 && targetSample - bestPoint.sample < sampleRate * 10L) return (audioStart, audioStart + bestPoint.offset, bestPoint.sample / (double)sampleRate);
        var estimate = audioStart + (long)((file.Length - audioStart) * (totalSamples > 0 ? targetSample / (double)totalSamples : seconds / Math.Max(1, track.Duration)));
        var window = ReadAt(file, Math.Max(audioStart, estimate), 1 << 20);
        for (var index = 0; index + 16 < window.Length; index++)
        {
            if (window[index] != 0xFF || (window[index + 1] & 0xFE) != 0xF8) continue;
            if (FlacFrameSample(window, index, minBlockSize) is not { } frameSample) continue;
            return (audioStart, Math.Max(audioStart, estimate) + index, frameSample / (double)sampleRate);
        }
        return null;
    }

    // Validates a FLAC frame header (fields and CRC-8) and returns the frame's first sample number
    static long? FlacFrameSample(byte[] bytes, int start, int fixedBlockSize)
    {
        var variableBlocks = (bytes[start + 1] & 1) == 1;
        int blockSizeCode = bytes[start + 2] >> 4, rateCode = bytes[start + 2] & 0x0F, channelCode = bytes[start + 3] >> 4, sizeCode = (bytes[start + 3] >> 1) & 7;
        if (blockSizeCode == 0 || rateCode == 15 || channelCode > 10 || sizeCode == 3 || (bytes[start + 3] & 1) != 0) return null;
        var position = start + 4;
        // UTF-8-style coded frame or sample number
        int leadByte = bytes[position], extraBytes = leadByte < 0x80 ? 0 : leadByte >= 0xFE ? 6 : leadByte >= 0xFC ? 5 : leadByte >= 0xF8 ? 4 : leadByte >= 0xF0 ? 3 : leadByte >= 0xE0 ? 2 : leadByte >= 0xC0 ? 1 : -1;
        if (extraBytes < 0) return null;
        long codedNumber = extraBytes == 0 ? leadByte : leadByte & (0x3F >> extraBytes);
        for (var extra = 1; extra <= extraBytes; extra++) { if ((bytes[position + extra] & 0xC0) != 0x80) return null; codedNumber = codedNumber << 6 | (long)(bytes[position + extra] & 0x3F); }
        position += 1 + extraBytes;
        if (blockSizeCode == 6) position += 1; else if (blockSizeCode == 7) position += 2;
        if (rateCode == 12) position += 1; else if (rateCode is 13 or 14) position += 2;
        byte crc = 0;
        for (var index = start; index < position; index++) { crc ^= bytes[index]; for (var bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1); }
        if (crc != bytes[position]) return null;
        return variableBlocks ? codedNumber : codedNumber * Math.Max(1, fixedBlockSize);
    }

    public static (long, long, double)? Wav(Stream file, OfflineTrack track, double seconds)
    {
        if (Encoding.ASCII.GetString(ReadAt(file, 0, 4)) != "RIFF") return null;
        long position = 12; int blockAlign = 0, sampleRate = 0;
        while (position + 8 <= file.Length)
        {
            var chunk = ReadAt(file, position, 8); var chunkId = Encoding.ASCII.GetString(chunk, 0, 4); var chunkLength = BitConverter.ToUInt32(chunk, 4);
            if (chunkId == "fmt ") { var format = ReadAt(file, position + 8, 16); sampleRate = BitConverter.ToInt32(format, 4); blockAlign = BitConverter.ToInt16(format, 12); }
            if (chunkId == "data") return blockAlign == 0 || sampleRate == 0 ? null : (position + 8, position + 8 + (long)(seconds * sampleRate) * blockAlign, (long)(seconds * sampleRate) / (double)sampleRate);
            position += 8 + chunkLength + (chunkLength & 1);
        }
        return null;
    }

    public static (long, long, double)? Aiff(Stream file, OfflineTrack track, double seconds)
    {
        if (Encoding.ASCII.GetString(ReadAt(file, 0, 4)) != "FORM" || track.SampleRate == 0) return null;
        static uint BigEndian(byte[] bytes, int offset) => (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
        long position = 12; var blockAlign = 0;
        while (position + 8 <= file.Length)
        {
            var chunk = ReadAt(file, position, 8); var chunkId = Encoding.ASCII.GetString(chunk, 0, 4); var chunkLength = BigEndian(chunk, 4);
            if (chunkId == "COMM") { var common = ReadAt(file, position + 8, 8); blockAlign = (common[0] << 8 | common[1]) * (((common[6] << 8 | common[7]) + 7) / 8); }
            if (chunkId == "SSND")
            {
                var dataStart = position + 16 + BigEndian(ReadAt(file, position + 8, 4), 0);
                var frames = (long)(seconds * track.SampleRate);
                return blockAlign == 0 ? null : (dataStart, dataStart + frames * blockAlign, frames / (double)track.SampleRate);
            }
            position += 8 + chunkLength + (chunkLength & 1);
        }
        return null;
    }

    // Squeezelite's PCM codes: size '0'-'3' (8-32 bit), rate as an index into its table, channels '1'/'2', byte order '0' big / '1' little
    static readonly int[] pcmRates = { 11025, 22050, 32000, 44100, 48000, 8000, 12000, 16000, 24000, 96000, 88200, 176400, 192000, 352800, 384000, 705600, 768000 };
    public static byte[]? PcmFormat(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var head = ReadAt(file, 0, 12); var kind = Encoding.ASCII.GetString(head, 0, 4);
            if (kind is not ("RIFF" or "FORM")) return null;
            static uint BigEndian(byte[] bytes, int offset) => (uint)(bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3]);
            long position = 12;
            while (position + 8 <= file.Length)
            {
                var chunk = ReadAt(file, position, 8); var chunkId = Encoding.ASCII.GetString(chunk, 0, 4); var chunkLength = kind == "RIFF" ? BitConverter.ToUInt32(chunk, 4) : BigEndian(chunk, 4);
                int channels = 0, bits = 0, rate = 0;
                if (kind == "RIFF" && chunkId == "fmt ") { var format = ReadAt(file, position + 8, 16); channels = BitConverter.ToInt16(format, 2); rate = BitConverter.ToInt32(format, 4); bits = BitConverter.ToInt16(format, 14); }
                if (kind == "FORM" && chunkId == "COMM")
                {
                    var common = ReadAt(file, position + 8, 18); channels = common[0] << 8 | common[1]; bits = common[6] << 8 | common[7];
                    var exponent = ((common[8] & 0x7F) << 8 | common[9]) - 16383; var mantissa = BigEndian(common, 10); // 80-bit extended sample rate
                    rate = (int)Math.Round(mantissa * Math.Pow(2, exponent - 31));
                }
                if (channels > 0)
                {
                    var rateIndex = Array.IndexOf(pcmRates, rate);
                    if (rateIndex < 0 || channels > 2 || bits is not (8 or 16 or 24 or 32)) return null;
                    return new[] { (byte)('0' + bits / 8 - 1), (byte)('0' + rateIndex), (byte)('0' + channels), (byte)(kind == "RIFF" ? '1' : '0') };
                }
                position += 8 + chunkLength + (chunkLength & 1);
            }
        }
        catch { }
        return null;
    }

    // MP3 decoders find the next frame themselves; the estimate is by bytes after the ID3 tag
    public static (long, long, double)? Mp3(Stream file, OfflineTrack track, double seconds)
    {
        if (track.Duration <= 0) return null;
        var audioStart = Id3Length(file);
        return (0, audioStart + (long)((file.Length - audioStart) * seconds / track.Duration), seconds);
    }
}
