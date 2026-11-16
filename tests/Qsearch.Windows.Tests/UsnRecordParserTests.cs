using Qsearch.Windows.Tests.Fixtures;
using Qsearch.Windows.Usn;

namespace Qsearch.Windows.Tests;

public sealed class UsnRecordParserTests
{
    [Fact]
    public void Parse_MalformedRecordLength_IsDiagnosticNotOutOfBounds()
    {
        byte[] buffer = UsnBufferBuilder.WithV2Record("bad.exe", recordLength: 4);

        UsnParseResult result = UsnRecordParser.Parse(buffer);

        Assert.Empty(result.Records);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "invalid-record-length");
    }

    [Fact]
    public void Parse_V2AndV3_PreserveOriginalUtf16NamesAndReferences()
    {
        byte[] v2 = UsnBufferBuilder.WithV2Record("گزارش\u202Efdp.exe", fileReference: 12, parentReference: 4);
        byte[] v3 = UsnBufferBuilder.WithV3Record("یادداشت.txt", fileReferenceLow: 21, fileReferenceHigh: 22);

        UsnRecord first = Assert.Single(UsnRecordParser.Parse(v2).Records);
        UsnRecord second = Assert.Single(UsnRecordParser.Parse(v3).Records);

        Assert.Equal("گزارش\u202Efdp.exe", first.Name);
        Assert.Equal(new FileReferenceId(12), first.FileReferenceNumber);
        Assert.Equal("یادداشت.txt", second.Name);
        Assert.Equal(new FileReferenceId(21, 22), second.FileReferenceNumber);
    }

    [Fact]
    public void Parse_UnknownVersion_IsDiagnosticAndSkipsBoundedRecord()
    {
        byte[] buffer = UsnBufferBuilder.WithV2Record("future.bin", majorVersion: 9);

        UsnParseResult result = UsnRecordParser.Parse(buffer);

        Assert.Empty(result.Records);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "unsupported-record-version");
    }

    [Fact]
    public void Parse_LongName_RemainsBoundedAndComplete()
    {
        string longName = new('ژ', 255);

        UsnRecord record = Assert.Single(
            UsnRecordParser.Parse(UsnBufferBuilder.WithV2Record(longName)).Records);

        Assert.Equal(longName, record.Name);
    }
}
