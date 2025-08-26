namespace Bayt_API.Testing;

public class ParsingMethodsTests
{
	[Fact]
	public void TestTextSlug()
	{
		const string goodTest = "This is a good test slug.";
		const string punctuationTest = "Hello!!! How are you??? (typing)";
		const string numbersTest = "1234567890";
		const string tooLongTest = "This is a test string that is too long to be a slug. It should be truncated to 32 characters.";

		Assert.Equal("this-is-a-good-test-slug", ParsingMethods.ConvertTextToSlug(goodTest));
		Assert.Equal("hello-how-are-you-typing", ParsingMethods.ConvertTextToSlug(punctuationTest));
		Assert.Equal("1234567890", ParsingMethods.ConvertTextToSlug(numbersTest));
		Assert.Equal("this-is-a-test-string-that-is-to", ParsingMethods.ConvertTextToSlug(tooLongTest));
	}
}