using System.Text.Json;

namespace Bayt_API.Testing;

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
		Assert.Equal("", ParsingMethods.ConvertTextToSlug(emptyTest));
		Assert.Equal("", ParsingMethods.ConvertTextToSlug(whitespaceTest));
		Assert.Equal("", ParsingMethods.ConvertTextToSlug(nullTest));
		Assert.Equal("test-for-trailing-whitespace", ParsingMethods.ConvertTextToSlug(trailingWhitespaceTest));
		Assert.Equal("test-for-leading-whitespace", ParsingMethods.ConvertTextToSlug(leadingWhitespaceTest));
		Assert.Equal("both-whitespace-tests", ParsingMethods.ConvertTextToSlug(leadingAndTrailingWhitespaceTest));
		Assert.Equal("this-should-fit-wo-whitespace", ParsingMethods.ConvertTextToSlug(whitespaceWhichWouldBeTooLongTest));
	}

	[Fact]
	public void ParseNonExistentFile()
	{
		Assert.Null(ParsingMethods.TryReadFile<long>("/theres/No/Way/This/Exists"));
	}
	[Fact]
	public void ParseValidFile()
	{
		var tempFileName = Path.GetTempFileName();
		File.WriteAllText(tempFileName, "1234567890");
		Assert.Equal(1234567890L, ParsingMethods.TryReadFile<long>(tempFileName));
		File.Delete(tempFileName);
	}
	[Fact]
	public void ParseInvalidFile()
	{
		var tempFileName = Path.GetTempFileName();
		File.WriteAllText(tempFileName, "This is not a number.");
		Assert.Null(ParsingMethods.TryReadFile<long>(tempFileName));
		File.Delete(tempFileName);
	}
	[Fact]
	public void ParseNullFile()
	{
		var tempFileName = Path.GetTempFileName();
		File.WriteAllText(tempFileName, "null");
		Assert.Null(ParsingMethods.TryReadFile<byte>(tempFileName));
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
}