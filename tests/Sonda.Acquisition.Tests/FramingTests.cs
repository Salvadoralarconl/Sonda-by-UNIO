using System.Text;
using Sonda.Application.Acquisition;
using Xunit;
namespace Sonda.Acquisition.Tests;
public sealed class FramingTests
{
    [Theory][InlineData(FileEncoding.Utf8)][InlineData(FileEncoding.Utf16LittleEndian)][InlineData(FileEncoding.Utf16BigEndian)]
    public void Every_split_preserves_exact_bytes_offsets_and_partial_tail(FileEncoding kind)
    {
        Encoding encoding = kind switch { FileEncoding.Utf8 => new UTF8Encoding(true, true), FileEncoding.Utf16LittleEndian => new UnicodeEncoding(false, true, true), _ => new UnicodeEncoding(true, true, true) };
        var content = encoding.GetPreamble().Concat(encoding.GetBytes("α😀\r\n\nidentical\nidentical\npartial😀")).ToArray();
        for (var split = 0; split <= content.Length; split++)
        {
            var framer = new ByteLineFramer(kind); var first = framer.Append(content.AsSpan(0, split)); var second = framer.Append(content.AsSpan(split));
            Assert.Null(first.Error); Assert.Null(second.Error); var records = first.Records.Concat(second.Records).ToArray();
            Assert.Equal(new[] { "α😀", "", "identical", "identical" }, records.Select(r => r.Text));
            Assert.Equal("CRLF", records[0].Terminator); Assert.Equal(encoding.GetPreamble().Length, records[0].BomBytes);
            Assert.Equal(content[..(int)records[^1].End], records.SelectMany(r => r.Bytes).ToArray());
            Assert.Equal(0, records[0].Start); Assert.Equal(records[0].End, records[1].Start);
            Assert.Equal(content.Length - records[^1].End, second.PartialBytes);
            var generation = Guid.NewGuid(); Assert.NotEqual(records[2].EvidenceKey("s", generation), records[3].EvidenceKey("s", generation));
            var restarted = new ByteLineFramer(kind, records[^1].End);
            Assert.Empty(restarted.Append(content.AsSpan((int)records[^1].End)).Records);
            Assert.Equal("partial😀", Assert.Single(restarted.Append(encoding.GetBytes("\n")).Records).Text);
        }
    }
    [Fact] public void Byte_at_a_time_bom_and_code_units_do_not_emit_partial_records()
    {
        var bytes = new byte[] {0xEF,0xBB,0xBF,0xF0,0x9F,0x98,0x80,13,10}; var f = new ByteLineFramer(FileEncoding.Utf8);
        foreach(var b in bytes[..^1])Assert.Empty(f.Append([b]).Records);
        var record=Assert.Single(f.Append([bytes[^1]]).Records); Assert.Equal("😀",record.Text);Assert.Equal(bytes,record.Bytes);
    }
    [Fact] public void Malformed_or_wrong_bom_and_over_limit_fail_without_skipping()
    {
        Assert.Equal("InvalidEncoding",new ByteLineFramer(FileEncoding.Utf8).Append([0xFF,10]).Error!.Code);
        Assert.Equal("BomMismatch",new ByteLineFramer(FileEncoding.Utf8).Append([0xFF,0xFE,10,0]).Error!.Code);
        var f=new ByteLineFramer(FileEncoding.Utf8,maximumRecordBytes:4);var b=f.Append(Encoding.UTF8.GetBytes("12345\n"));
        Assert.Empty(b.Records);Assert.Equal("RecordTooLarge",b.Error!.Code);Assert.Equal(4,b.PartialBytes);Assert.Empty(f.Append([10]).Records);
    }
    [Fact] public void Prior_complete_records_survive_later_decode_error_in_same_read()
    {
        var b=new ByteLineFramer(FileEncoding.Utf8).Append([65,10,0xFF,10]);Assert.Equal("A",Assert.Single(b.Records).Text);Assert.Equal(2,b.Error!.Offset);
    }
}
