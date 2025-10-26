namespace Bayt_API.Testing;

public class LoggingTests
{
	private static readonly byte[] ReferenceHeader = [
		5, 15, 0, 255, 63, 55, 244, 117, 40, 202, 43, 84, 101, 115, 116, 105, 110, 103, 2
	];
	private static readonly byte[] ReferenceContent = "This is a test!"u8.ToArray();

	[Fact]
	public void TestParsingFull()
	{
		byte[] referenceArray = ReferenceHeader.Concat(ReferenceContent).ToArray();

		var entry = LogEntry.Parse(referenceArray);

		Assert.Equal(StreamId.Info, entry.StreamId);
		Assert.Equal("Testing", entry.ModuleName);
		Assert.Equal("This is a test!", entry.Content);
		Assert.Equal(DateTime.MaxValue, entry.TimeWritten);
	}
	[Fact]
	public void TestParsingSplit()
	{
		var entry = LogEntry.Parse(ReferenceHeader, ReferenceContent);

		Assert.Equal(StreamId.Info, entry.StreamId);
		Assert.Equal("Testing", entry.ModuleName);
		Assert.Equal("This is a test!", entry.Content);
		Assert.Equal(DateTime.MaxValue, entry.TimeWritten);
	}
	[Fact]
	public void TestSerialization()
	{
		var serializedEntry = new LogEntry(StreamId.Info, "Testing", "This is a test!", DateTime.MaxValue).Serialize();


		Assert.Equal(ReferenceHeader, serializedEntry.AsSpan(0, ReferenceHeader.Length).ToArray());
		Assert.Equal(ReferenceContent, serializedEntry.AsSpan(ReferenceHeader.Length).ToArray());
	}

	[Fact]
	public void TestControlCharacterFiltering()
	{
		var serializedEntry = new LogEntry(StreamId.Info, "\u0002T\res\nt\fi\tn\bg\v", "\n\t\r\u0002This i\fs a\v te\bst!\a", DateTime.MaxValue).Serialize();


		Assert.Equal(ReferenceHeader, serializedEntry.AsSpan(0, ReferenceHeader.Length).ToArray());
		Assert.Equal(ReferenceContent, serializedEntry.AsSpan(ReferenceHeader.Length).ToArray());
	}
}