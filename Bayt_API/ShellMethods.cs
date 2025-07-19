using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Bayt_API;

// This class was mostly written by Gemini. TODO: Rewrite this yourself.

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
	public int ExitCode { get; init; }

	/// <summary>
	/// Contains a value indicating whether the process completed successfully.
	/// </summary>
	public bool Success => ExitCode == 0;
}

public static class ShellMethods
{
	/// <summary>
	/// Runs an external program and captures its output.
	/// </summary>
	/// <param name="program">The path to the program to execute.</param>
	/// <param name="arguments">The command-line arguments to pass to the program.</param>
	/// <param name="timeoutMilliseconds">The maximum time to wait for the process to exit, in milliseconds.
	/// Defaults to 1.5 seconds.</param>
	/// <returns>A <see cref="ShellResult"/> containing the process output, error, and exit code.</returns>
	/// <exception cref="InvalidOperationException">Thrown if there is an error starting the process.</exception>
	/// <exception cref="TimeoutException">Thrown if the process does not exit within the specified timeout.</exception>
	public static ShellResult RunShell(string program, string arguments = "", int timeoutMilliseconds = 1500)
	{
		using var process = new Process();
		process.StartInfo = new ProcessStartInfo
        {
            FileName = program,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // Use AutoResetEvent to signal when streams have finished closing
        using var outputWaitHandle = new AutoResetEvent(false);
        using var errorWaitHandle = new AutoResetEvent(false);

        // Assign handlers using local functions
        process.OutputDataReceived += ProcessOutputDataReceived;
        process.ErrorDataReceived += ProcessErrorDataReceived;

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Wrap other potential exceptions during process start
            throw new InvalidOperationException($"Failed to start process '{program}'. See inner exception.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for the process to exit and for the stream handlers to signal completion
        bool processExited = process.WaitForExit(timeoutMilliseconds);
        bool outputStreamClosed = outputWaitHandle.WaitOne(timeoutMilliseconds); // Consider a shorter/separate timeout?
        bool errorStreamClosed = errorWaitHandle.WaitOne(timeoutMilliseconds);  // Consider a shorter/separate timeout?

        if (processExited && outputStreamClosed && errorStreamClosed)
        {
            // Process completed normally
            return new ShellResult
            {
                StandardOutput = outputBuilder.ToString().TrimEnd('\n'),
                StandardError = errorBuilder.ToString().TrimEnd('\n'),
                ExitCode = process.ExitCode // Get the actual exit code
            };
        }
        // Timeout occurred
        KillProcessSafe(process, program); // Attempt to kill the lingering process

        // Construct a meaningful timeout message
        string timeoutMessage = $"Process '{program} {arguments}' timed out after {timeoutMilliseconds} ms.";
        if (!processExited) timeoutMessage += " Process did not exit.";
        if (!outputStreamClosed) timeoutMessage += " Output stream reading did not complete.";
        if (!errorStreamClosed) timeoutMessage += " Error stream reading did not complete.";
        timeoutMessage += $" Output captured: '{outputBuilder}'. Error captured: '{errorBuilder}'.";

        throw new TimeoutException(timeoutMessage);

        // --- Local Handler Methods ---
        void ProcessOutputDataReceived(object sender, DataReceivedEventArgs e) =>
            HandleStreamData(e, outputBuilder, outputWaitHandle);

        void ProcessErrorDataReceived(object sender, DataReceivedEventArgs e) =>
            HandleStreamData(e, errorBuilder, errorWaitHandle);

        static void HandleStreamData(DataReceivedEventArgs e, StringBuilder builder, AutoResetEvent waitHandle)
        {
            if (e.Data == null)
            {
                // End of stream
                try
                {
                    waitHandle.Set(); // Signal that this stream is done
                }
                catch (ObjectDisposedException)
                {
                    // Ignore if the handle was already disposed (e.g., due to timeout/cleanup)
                    Debug.WriteLine("WaitHandle disposed before stream handler could signal completion.");
                }
            }
            else
            {
                // Append data. Lock for potential concurrent writes, although unlikely for standard handlers.
                lock (builder)
                {
                    builder.AppendLine(e.Data);
                }
            }
        }

        // --- Helper to safely kill the process ---
        static void KillProcessSafe(Process process, string programName)
        {
             try
             {
	             // Check HasExited before attempting to kill to avoid exceptions
	             if (process.HasExited) return;

	             Debug.WriteLine($"Process '{programName}' timed out or streams did not close. Attempting to kill.");
                 process.Kill(true); // Kill process and its children
                 // Optionally wait a very short time for the kill operation
                 // process.WaitForExit(500);
                 Debug.WriteLine($"Process '{programName}' kill signal sent.");
             }
             catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
             {
                 // Log if killing failed (e.g., the process already exited or access denied)
                 Debug.WriteLine($"Failed to kill process '{programName}' after timeout: {ex.Message}");
             }
        }
	}


	/// <summary>
	/// Check if a script exists, is executable, and supports the required features.
	/// </summary>
	/// <param name="scriptPath">The path to the script file.</param>
	/// <param name="requiredSupports">List of features it's expected to support. Leave empty to skip feature checks.</param>
	/// <param name="loggingFeatureName">General feature name for use in logs. For example, "GPU stats" </param>
	/// <returns>Full array of the supported features.</returns>
	/// <exception cref="FileNotFoundException">The script file was not found or was not executable.</exception>
	/// <exception cref="FileLoadException">The script file exited with a non-zero exit code after being prompted for supports list.</exception>
	/// <exception cref="NotSupportedException">One or more of the requested features was not supported.</exception>
	[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
	public static string[] CheckScriptSupports(string scriptPath, List<string> requiredSupports, string loggingFeatureName)
	{
		if (!File.Exists(scriptPath) || (File.GetUnixFileMode(scriptPath) & UnixFileMode.UserExecute) == 0)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine(
				$"[ERROR] No executable script was found at '{scriptPath}' (did you remember to chmod +x it?)");
			Console.ResetColor();
			throw new FileNotFoundException($"Could not find an executable script at '{scriptPath}", scriptPath);
		}

		var supportsShell = RunShell(scriptPath, "testSupports");
		if (!supportsShell.Success)
		{
			Console.ForegroundColor = ConsoleColor.Red;
			Console.WriteLine($"[ERROR] Checking for shell supports failed with code: {supportsShell.ExitCode}. Your scripts may be out of date with your server binary.\n" +
			                  $"Path: {scriptPath}");
			Console.ResetColor();
			throw new FileLoadException($"Shell supports check failed with code: {supportsShell.ExitCode}. ({scriptPath})");
		}

		if (requiredSupports.Count == 0)
		{
			return [];
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

		if (requiredSupports.Count == 0)
		{
			return supportsList;
		}

		Console.ForegroundColor = ConsoleColor.Red;
		Console.WriteLine($"[ERROR] The script at '{scriptPath}' does not indicate support for a feature that was required. ('{loggingFeatureName}')");
		Console.ResetColor();

		throw new NotSupportedException($"The script at '{scriptPath}' does not indicate support for a feature that was required. ('{loggingFeatureName}')");
	}
}