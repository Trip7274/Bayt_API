namespace Bayt_API.Testing;

public class RequestCheckTests
{
	[Fact]
	public async Task TestDockerRequestValidation()
	{
		if (!DockerLocal.IsDockerAvailable) return; // It'd be annoying to *require* Docker just to pass tests.

		var nullValidationCheck = await RequestChecking.ValidateDockerRequest(null, false);
		Assert.NotNull(nullValidationCheck);

		var tooShortValidationCheck = await RequestChecking.ValidateDockerRequest("too short",false);
		Assert.NotNull(tooShortValidationCheck);

		var validValidationCheck = await RequestChecking.ValidateDockerRequest("i am a string much much longer than 12 characters", false);
		Assert.Null(validValidationCheck);
	}
}