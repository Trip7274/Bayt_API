namespace Sofa_API.UnitTests;

public class LoggingTests
{
	//private static readonly LogEntry ReferenceEntry = new (LogStream.Info, "Testing", "This is a test!", ReferenceDate);
	private static readonly byte[] ReferenceHeader = [
		5, 7, 15, 0, 128, 132, 81, 194, 255, 41, 221, 72, 84, 101, 115, 116, 105, 110, 103
	];
	private static readonly byte[] ReferenceContent = "This is a test!"u8.ToArray();
	private static byte[] FullReferenceEntry => ReferenceHeader.Concat(ReferenceContent).ToArray();
	private static readonly DateTime ReferenceDate = new(2025, 1, 1, 1, 1, 1, DateTimeKind.Utc);

	[Fact]
	public void TestParsingFull()
	{
		var entry = LogEntry.Parse(FullReferenceEntry);

		Assert.Equal(LogStream.Info, entry.LogStream);
		Assert.Equal("Testing", entry.ModuleName);
		Assert.Equal("This is a test!", entry.Content);
		Assert.Equal(ReferenceDate, entry.TimeWritten);
	}
	[Fact]
	public void TestParsingSplit()
	{
		var entry = LogEntry.Parse(ReferenceHeader, ReferenceContent);

		Assert.Equal(LogStream.Info, entry.LogStream);
		Assert.Equal("Testing", entry.ModuleName);
		Assert.Equal("This is a test!", entry.Content);
		Assert.Equal(ReferenceDate, entry.TimeWritten);
	}

	[Fact]
	public void TestParsingString()
	{
		const string referenceString = "1:01:01 AM +00  | INFO    | Testing                          | This is a test!";

		var dateToday = DateOnly.FromDateTime(DateTime.Today);
		var referenceDateToday = new DateTime(dateToday.Year, dateToday.Month, dateToday.Day, 1, 1, 1, DateTimeKind.Utc);

		var entry = LogEntry.Parse(referenceString);

		Assert.Equal(LogStream.Info, entry.LogStream);
		Assert.Equal("Testing", entry.ModuleName);
		Assert.Equal("This is a test!", entry.Content);
		Assert.Equal(referenceDateToday, entry.TimeWritten); // String parsing cannot on its own determine the DATE, as it's not serialized into string format.
	}
	[Fact]
	public void TestSerialization()
	{
		var serializedEntry = new LogEntry(LogStream.Info, "Testing", "This is a test!", ReferenceDate).Serialize();

		if (FullReferenceEntry.Equals(serializedEntry)) return;

		// This is just to split which part is incorrect.
		Assert.Equal(ReferenceHeader, serializedEntry.AsSpan(0, ReferenceHeader.Length).ToArray());
		Assert.Equal(ReferenceContent, serializedEntry.AsSpan(ReferenceHeader.Length).ToArray());
	}

	[Fact]
	public void TestControlCharacterFiltering()
	{
		var serializedEntry = new LogEntry(LogStream.Info, "\u0002T\res\nt\fi\tn\bg\v", "\n\t\r\u0002This i\fs a\v te\bst!\a", ReferenceDate).Serialize();

		if (FullReferenceEntry.Equals(serializedEntry)) return;

		// This is just to split which part is incorrect.
		Assert.Equal(ReferenceHeader, serializedEntry.AsSpan(0, ReferenceHeader.Length).ToArray());
		Assert.Equal(ReferenceContent, serializedEntry.AsSpan(ReferenceHeader.Length).ToArray());
	}
}