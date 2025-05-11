namespace Bayt_API.Testing;

public class ShellTests
{
	[Fact]
	public void ShellExitCode()
	{
		Assert.True(ShellMethods.RunShell("true").Success);
	}

	[Fact]
	public void ShellStdout()
	{
		Assert.Equal("This is to test stdout", ShellMethods.RunShell("echo", "This is to test stdout").StandardOutput.TrimEnd('\n'));
	}

	[Fact]
	public void ShellStderr()
	{
		Assert.Equal("This is to test stderr", ShellMethods.RunShell("sh", "-c \"echo 'This is to test stderr' >&2\"").StandardError.TrimEnd('\n'));
	}

	[Fact]
	public void ShellTimeout()
	{
		Assert.Throws<TimeoutException>(() => ShellMethods.RunShell("sleep", "0.2", 150));
	}
}