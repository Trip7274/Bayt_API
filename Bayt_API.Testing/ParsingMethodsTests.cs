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
}