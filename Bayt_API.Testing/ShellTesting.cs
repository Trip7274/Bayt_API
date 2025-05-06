namespace Bayt_API.Testing;

public class ShellTesting
{
	[Fact]
	public void TestShellExitCode()
	{
		Assert.True(ShellMethods.RunShell("true").Success);
	}

	[Fact]
	public void TestShellStdout()
	{
		Assert.Equal("This is to test stdout", ShellMethods.RunShell("echo", "This is to test stdout").StandardOutput.TrimEnd('\n'));
	}

	// TODO: Figure out a way to test stderr

	[Fact]
	public void TestShellTimeout()
	{
		Assert.Throws<TimeoutException>(() => ShellMethods.RunShell("sleep", "0.2", 150));
	}
}