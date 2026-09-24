using System.Text;

namespace Sonda.Application.Acquisition;

/// <summary>Frames exact byte ranges. EOF never flushes an incomplete record.</summary>
public sealed class ByteLineFramer
{
    private readonly FileEncoding kind;
    private readonly Encoding decoder;
    private readonly int maximum;
    private readonly List<byte> pending = [];
    private long start;
    private int scan, bom;
    private bool bomResolved;
    private FramingError? error;
    public int PartialBytes => pending.Count;
    public ByteLineFramer(FileEncoding encoding, long offset = 0, int maximumRecordBytes = 1048576)
    {
        if (offset < 0 || maximumRecordBytes < 4) throw new ArgumentOutOfRangeException(nameof(offset));
        kind = encoding; start = offset; maximum = maximumRecordBytes; bomResolved = offset != 0;
        decoder = encoding switch
        {
            FileEncoding.Utf8 => new UTF8Encoding(false, true),
            FileEncoding.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
            FileEncoding.Utf16BigEndian => new UnicodeEncoding(true, false, true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding))
        };
        if (kind != FileEncoding.Utf8 && offset % 2 != 0) throw new ArgumentException("UTF-16 checkpoint must be code-unit aligned.");
    }
    public FramingBatch Append(ReadOnlySpan<byte> input)
    {
        List<PhysicalRecord> records = [];
        if (error is not null) return new([], error, pending.Count);
        foreach (var value in input)
        {
            if (pending.Count == maximum) { error = new("RecordTooLarge", start, "Record exceeds configured byte bound."); break; }
            pending.Add(value);
            if (!bomResolved && !ResolveBom()) { if (error is not null) break; continue; }
            var width = kind == FileEncoding.Utf8 ? 1 : 2;
            while (scan + width <= pending.Count)
            {
                var unit = Unit(scan); scan += width;
                if (unit != 10) continue;
                var crlf = scan - width * 2 >= bom && Unit(scan - width * 2) == 13;
                var textLength = scan - bom - width * (crlf ? 2 : 1);
                var bytes = pending.ToArray();
                try
                {
                    var text = decoder.GetString(bytes, bom, textLength);
                    records.Add(new(start, checked(start + bytes.Length), bytes, text, crlf ? "CRLF" : "LF", bom));
                }
                catch (DecoderFallbackException) { error = new("InvalidEncoding", start, "Record is not valid in the configured encoding."); break; }
                start = checked(start + bytes.Length); pending.Clear(); scan = 0; bom = 0; break;
            }
            if (error is not null) break;
        }
        return new(records.ToArray(), error, pending.Count);
    }
    private int Unit(int at) => kind switch
    {
        FileEncoding.Utf8 => pending[at],
        FileEncoding.Utf16LittleEndian => pending[at] | pending[at + 1] << 8,
        _ => pending[at] << 8 | pending[at + 1]
    };
    private bool ResolveBom()
    {
        byte[][] signatures = [[0xEF, 0xBB, 0xBF], [0xFF, 0xFE], [0xFE, 0xFF]];
        for (var n = 0; n < signatures.Length; n++)
        {
            var signature = signatures[n];
            if (!pending.Take(Math.Min(pending.Count, signature.Length)).SequenceEqual(signature.Take(Math.Min(pending.Count, signature.Length)))) continue;
            if (pending.Count < signature.Length) return false;
            if ((int)kind != n) { error = new("BomMismatch", start, "BOM conflicts with configured encoding."); return false; }
            bom = signature.Length; scan = bom; bomResolved = true; return true;
        }
        bomResolved = true; return true;
    }
}
