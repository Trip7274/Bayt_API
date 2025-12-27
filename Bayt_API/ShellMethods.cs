using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using CliWrap;

namespace Bayt_API;


/// <summary>
/// Represents the result of executing a shell command.
/// </summary>
public class ShellResult
{
	/// <summary>
	/// Contains the standard output produced by the process.
	/// </summary>
	public string StandardOutput { get; init; } = string.Empty;

	/// <summary>
	/// Contains the standard error output produced by the process.
	/// </summary>
	public string StandardError { get; init; } = string.Empty;

	/// <summary>
	/// Contains the exit code returned by the process.
	/// </summary>
	/// <remarks>
	///	The status code will be 124 if the process timed out, and 0 if it completed successfully.
	/// </remarks>
	public int ExitCode { get; init; }

	/// <summary>
	/// Contains a value indicating whether the process completed successfully.
	/// </summary>
	public bool IsSuccess => ExitCode == 0;
}

public static class ShellMethods
{
	/// <summary>
	/// Runs an external program and captures its output.
	/// </summary>
	/// <param name="program">The path to the program to execute.</param>
	/// <param name="arguments">The command-line arguments to pass to the program.</param>
	/// <param name="timeout">The maximum time to wait for the process to exit. Defaults to 5 seconds.</param>
	/// <param name="throwIfTimedout">Whether to throw a <c>TimeoutException</c> if the process times out, or return a ShellResult object with a status code of 124. Defaults to true.</param>
	/// <param name="environmentVariables">Environment variables to set for the specified process. These are applied over the Bayt API's env vars.</param>
	/// <returns>A <see cref="ShellResult"/> containing the process output, error, and exit code. Will be null if the process timed out.</returns>
	/// <exception cref="InvalidOperationException">Thrown if there is an error starting the process.</exception>
	/// <exception cref="TimeoutException">Thrown if the process does not exit within the specified timeout.</exception>
	public static async Task<ShellResult> RunShell(string program, string[]? arguments = null, TimeSpan? timeout = null, bool throwIfTimedout = true, Dictionary<string, string?>? environmentVariables = null)
	{
		var processIdentifier = (ushort) Random.Shared.Next();
		timeout ??= TimeSpan.FromSeconds(5);
		arguments ??= [];

		Logs.LogBook.Write(new LogEntry(StreamId.Verbose, $"Process Execution [{processIdentifier:X4}]", $"Got a request to run a command: '{Path.GetFileName(program)} [{string.Join(", ", arguments)}]'"));
		StringBuilder stdout = new();
		StringBuilder stderr = new();
		Dictionary<string, string?> envVars = new()
		{
			{ "BAYT_SUBPROCESS", "1" }
		};
		if (environmentVariables != null)
		{
			foreach (var environmentVariable in environmentVariables)
			{
				envVars.Add(environmentVariable.Key, environmentVariable.Value);
			}
		}

		var statusCode = -1;
		CommandResult? process = null;
		Stopwatch processTimer = new();
		try
		{
			processTimer.Start();
			process = await Cli.Wrap(program)
				.WithArguments(arguments)
				.WithValidation(CommandResultValidation.None)
				.WithStandardOutputPipe(PipeTarget.ToStringBuilder(stdout))
				.WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
				.WithWorkingDirectory(Directory.GetCurrentDirectory())
				.WithEnvironmentVariables(envVars)
				.ExecuteAsync(new CancellationTokenSource(timeout.Value).Token);
			processTimer.Stop();
		}
		catch (OperationCanceledException e)
		{
			if (throwIfTimedout)
			{
				Logs.LogBook.Write(new(StreamId.Fatal,
					$"Process Execution [{processIdentifier:X4}]", $"The process '{Path.GetFileName(program)}' fatally timed out after {processTimer.Elapsed.TotalSeconds} seconds. Bayt will exit."));
				throw new TimeoutException(
					$"[{processIdentifier:X4}] The process '{Path.GetFileName(program)}' timed out after {processTimer.Elapsed.TotalSeconds} seconds.",
					e);
			}

			statusCode = 124; // The code is from the `timeout` command in the GNU coreutils.
		}
		if (statusCode == -1 && process is not null) statusCode = process.ExitCode;

		Logs.LogBook.Write(new LogEntry(StreamId.Verbose, $"Process Execution [{processIdentifier:X4}]",
			$"Process '{Path.GetFileName(program)}' exited with code {statusCode}. (in {Math.Round(process?.RunTime.TotalMilliseconds ?? processTimer.ElapsedMilliseconds, 2)}ms)"));
		return new ShellResult
		{
			StandardOutput = stdout.ToString().Trim('\n'),
			StandardError = stderr.ToString().Trim('\n'),
			ExitCode = statusCode
		};
	}


	/// <summary>
	/// Check if a script exists, is executable, and supports the required features. Returns all the supported features of the script.
	/// </summary>
	/// <param name="scriptPath">The path to the script file.</param>
	/// <returns>Array of all announced features. Empty if any check failed.</returns>
	/// <seealso cref="CheckScriptSupports"/>
	[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
	public static string[] GetScriptSupports(string scriptPath)
	{
		if (!File.Exists(scriptPath) || (File.GetUnixFileMode(scriptPath) & UnixFileMode.UserExecute) == 0)
		{
			throw new FileNotFoundException($"The file '{scriptPath}' does not exist or is not executable.");
		}

		var supportsShell = RunShell(scriptPath, ["Meta.Supports"]).Result;
		if (!supportsShell.IsSuccess)
		{
			throw new Exception($"The script '{scriptPath}' failed to execute. ({supportsShell.ExitCode})");
		}

		string[] supportsList = supportsShell.StandardOutput.Trim('|').Split('|');

		return supportsList;

	}

	/// <summary>
	/// Check if a script exists, is executable, and supports the required features. Returns false if any checks fail, otherwise true.
	/// </summary>
	/// <param name="scriptPath">The path to the script file.</param>
	/// <param name="requiredSupports">List of features it's expected to support. Leave empty to skip feature checks.</param>
	/// <returns>Whether the script passed all the checks.</returns>
	/// <seealso cref="GetScriptSupports"/>
	[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
	public static bool CheckScriptSupports(string scriptPath, List<string> requiredSupports)
	{
		if (!File.Exists(scriptPath) || (File.GetUnixFileMode(scriptPath) & UnixFileMode.UserExecute) == 0)
		{
			return false;
		}

		var supportsShell = RunShell(scriptPath, ["Meta.Supports"]).Result;
		if (!supportsShell.IsSuccess)
		{
			return false;
		}

		if (requiredSupports.Count == 0)
		{
			return true;
		}

		string[] supportsList = supportsShell.StandardOutput.Trim('|').Split('|');

		byte index = 0;
		foreach (var supportsEntry in supportsList)
		{
			if (supportsEntry == requiredSupports[index])
			{
				requiredSupports.RemoveAt(index);
			}
			index++;
		}

		return requiredSupports.Count == 0;

	}
}
