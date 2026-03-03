using System.Text.Json;

namespace Bayt_API.UnitTests;

public class ParsingMethodsTests
{
	[Fact]
	public void TestTextSlug()
	{
		const string goodTest = "This is a good test slug.";
		const string punctuationTest = "Hello!!! How are you??? (typing)";
		const string symbolsTest = "!@#$%^&*()_+=-[]\\{}|;':\",./<>?";
		const string numbersTest = "1234567890";
		const string indianNumbersTest = "١٢٣٤٥٦٧٨٩٠";
		const string tooLongTest = "This is a test string that is too long to be a slug. It should be truncated to 32 characters.";
		const string emptyTest = "";
		const string whitespaceTest = "		 ";
		const string? nullTest = null;
		const string trailingWhitespaceTest = "Test for trailing whitespace.		 ";
		const string leadingWhitespaceTest = "		  Test for leading whitespace.";
		const string leadingAndTrailingWhitespaceTest = " 		  Both whitespace tests. 	 	 ";
		const string whitespaceWhichWouldBeTooLongTest = " 		  This should fit w/o whitespace. 	 	 ";

		Assert.Equal("this-is-a-good-test-slug", ParsingMethods.ConvertTextToSlug(goodTest));
		Assert.Equal("hello-how-are-you-typing", ParsingMethods.ConvertTextToSlug(punctuationTest));
		Assert.Equal("", ParsingMethods.ConvertTextToSlug(symbolsTest));
		Assert.Equal("1234567890", ParsingMethods.ConvertTextToSlug(numbersTest));
		Assert.Equal("", ParsingMethods.ConvertTextToSlug(indianNumbersTest));
		Assert.Equal("this-is-a-test-string-that-is-to", ParsingMethods.ConvertTextToSlug(tooLongTest));
		Assert.Equal(32, ParsingMethods.ConvertTextToSlug(tooLongTest).Length);

		Assert.Throws<ArgumentNullException>(() => ParsingMethods.ConvertTextToSlug(emptyTest));
		Assert.Throws<ArgumentNullException>(() => ParsingMethods.ConvertTextToSlug(whitespaceTest));
		Assert.Throws<ArgumentNullException>(() => ParsingMethods.ConvertTextToSlug(nullTest));

		Assert.Equal("test-for-trailing-whitespace", ParsingMethods.ConvertTextToSlug(trailingWhitespaceTest));
		Assert.Equal("test-for-leading-whitespace", ParsingMethods.ConvertTextToSlug(leadingWhitespaceTest));
		Assert.Equal("both-whitespace-tests", ParsingMethods.ConvertTextToSlug(leadingAndTrailingWhitespaceTest));
		Assert.Equal("this-should-fit-wo-whitespace", ParsingMethods.ConvertTextToSlug(whitespaceWhichWouldBeTooLongTest));
	}

	[Fact]
	public void ParseNonExistentFile()
	{
		Assert.Null(ParsingMethods.TryReadFile<byte>("/theres/No/Way/This/Exists"));
	}
	[Fact]
	public async Task ParseValidFile()
	{
		var tempFileName = Path.GetTempFileName();
		await File.WriteAllTextAsync(tempFileName, "128");
		Assert.Equal((byte) 128, ParsingMethods.TryReadFile<byte>(tempFileName));
		Assert.Equal((byte) 128, await ParsingMethods.TryReadFileAsync<byte>(tempFileName));
		File.Delete(tempFileName);
	}
	[Fact]
	public async Task ParseInvalidFile()
	{
		var tempFileName = Path.GetTempFileName();
		await File.WriteAllTextAsync(tempFileName, "This is not a number.");
		Assert.Null(ParsingMethods.TryReadFile<byte>(tempFileName));
		Assert.Null(await ParsingMethods.TryReadFileAsync<byte>(tempFileName));
		File.Delete(tempFileName);
	}
	[Fact]
	public async Task ParseNullFile()
	{
		var tempFileName = Path.GetTempFileName();
		await File.WriteAllTextAsync(tempFileName, "null");
		Assert.Null(ParsingMethods.TryReadFile<byte>(tempFileName));
		Assert.Null(await ParsingMethods.TryReadFileAsync<byte>(tempFileName));
		File.Delete(tempFileName);
	}

	[Fact]
	public void ParseValidJsonElement()
	{
		var jsonElement = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"Test\": true}")!;
		Assert.Equal(true, jsonElement["Test"].ParseNullable<bool>());
	}
	[Fact]
	public void ParseNullJsonElement()
	{
		var jsonElement = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"Test\": null}")!;
		Assert.Null(jsonElement["Test"].ParseNullable<bool>());
	}
	[Fact]
	public void ParseIncorrectJsonElement()
	{
		var jsonElement = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("{\"Test\": true}")!;
		Assert.Null(jsonElement["Test"].ParseNullable<byte>());
	}

	[Fact]
	public void SanitizeCorrectString()
	{
		const string testString = "This is a test string.";

		Assert.Equal(testString, ParsingMethods.SanitizeString(testString));
	}

	[Fact]
	public void SanitizeEmptyString()
	{
		const string testString = "";

		Assert.Equal("", ParsingMethods.SanitizeString(testString));
	}

	[Fact]
	public void SanitizeDirtyString()
	{
		const string testString = "This is a test string! 😄";

		Assert.Equal("This is a test string! ", ParsingMethods.SanitizeString(testString));
	}

	[Fact]
	public void SanitizeFullyDirtyString()
	{
		// Sanitize a string full of non-ASCII characters.
		const string testString = "هذاتجربة😄";

		Assert.Equal("", ParsingMethods.SanitizeString(testString));
	}

	[Fact]
	public void SanitizeControlCharacters()
	{
		// Test a string full of control characters.
		const string testString = "\e\u0001\n\r\n\a\b\0\u0018";

		Assert.Equal("", ParsingMethods.SanitizeString(testString));
	}

	[Fact]
	public void SanitizeAnsiEscapeCodes()
	{
		const string testString = "\e[0;32mHello!\e[0m";

		Assert.Equal("[0;32mHello![0m", ParsingMethods.SanitizeString(testString));
	}
}