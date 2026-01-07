namespace Bayt_API.UnitTests;

public class ShellTests
{
	[Fact]
	public async Task ShellExitCode()
	{
		Assert.True((await ShellMethods.RunShell("true")).IsSuccess);
	}

	[Fact]
	public async Task ShellStdout()
	{
		Assert.Equal("This is to test stdout", (await ShellMethods.RunShell("echo", ["This is to test stdout"])).StandardOutput);
	}

	[Fact]
	public async Task ShellStderr()
	{
		Assert.Equal("This is to test stderr", (await ShellMethods.RunShell("sh",
			["-c", "echo 'This is to test stderr' >&2"])).StandardError);
	}

	[Fact]
	public async Task ShellTimeout()
	{
		await Assert.ThrowsAsync<TimeoutException>(() => ShellMethods.RunShell("sleep", ["0.1"], TimeSpan.FromMicroseconds(1), true));
	}
	[Fact]
	public async Task NoShellTimeoutWhenRequested()
	{
		Assert.Equal(124, (await ShellMethods.RunShell("sleep", ["0.1"], TimeSpan.FromMicroseconds(1), false)).ExitCode);
	}
}