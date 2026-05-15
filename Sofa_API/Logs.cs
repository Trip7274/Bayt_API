using System.Text;

namespace Sofa_API;

public sealed class LogEntry
{
	// Header length: 13-42 bytes
	// Serialized Format:
	// 1 byte: Log Stream
	// 1 byte: Module Name Length
	// 2 bytes: Content Length
	// 8 bytes: Time Written
	// (1-32) bytes: Module Name
	// {Content Length} bytes: Content (1-1024 bytes/chars)
	private const ushort MaxContentLength = 1024;
	private const byte MaxModuleNameLength = 32;

	public LogEntry(LogStream logStream, string moduleName, string content, DateTime? timeWritten = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Log module name or content cannot be empty or whitespace.");

		LogStream = logStream;
		ModuleName = moduleName;
		Content = content;
		if (timeWritten is not null) TimeWritten = timeWritten.Value;
	}
	public LogEntry(byte logStreamByte, string moduleName, byte[] contentBytes, long? timeWrittenBinary = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || contentBytes.Length == 0) throw new ArgumentException("Log module name or content cannot be empty or whitespace.");

		LogStream = ParsingMethods.ClampToMaxLogStreamValue(logStreamByte);
		ModuleName = moduleName;
		ContentBytes = contentBytes;
		if (timeWrittenBinary is not null) TimeWritten = DateTime.FromBinary(timeWrittenBinary.Value);
	}

	public LogStream LogStream { get; }

	public ushort ContentLength => (ushort) int.Clamp(ContentBytes.Length, 0, MaxContentLength);

	public DateTime TimeWritten
	{
		get => DateTime.FromBinary(TimeWrittenBinary);
		private init => TimeWrittenBinary = value.ToUniversalTime().ToBinary();
	}
	public long TimeWrittenBinary { get; private init; } = DateTime.UtcNow.ToBinary();

	public string ModuleName
	{
		get => Encoding.ASCII.GetString(_moduleNameBytes);
		private init
		{
			if (value.Length > MaxModuleNameLength) value = value[..MaxModuleNameLength];

			value = ParsingMethods.SanitizeString(value);
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Module name cannot be empty or whitespace.");

			_moduleNameBytes = Encoding.ASCII.GetBytes(value);
		}
	}
	private readonly byte[] _moduleNameBytes = new byte[MaxModuleNameLength];

	public string Content
	{
		get => Encoding.ASCII.GetString(ContentBytes);
		private init
		{
			if (value.Length > MaxContentLength) value = value[..MaxContentLength];

			value = ParsingMethods.SanitizeString(value);
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Content cannot be null or whitespace.");

			ContentBytes = Encoding.ASCII.GetBytes(value);
		}
	}
	public byte[] ContentBytes
	{
		get;
		private init
		{
			if (value.Length > MaxContentLength) value = value[..MaxContentLength];
			field = value;
		}
	} = [];
	public ushort SerializedLength => (ushort) (12 + _moduleNameBytes.Length + ContentLength);

	public static LogEntry Parse(byte[] data)
	{
		if (data.Length < 14) throw new ArgumentOutOfRangeException(nameof(data), "Data is too short.");

		var logStream = data[0];
		var moduleNameLength = data[1];
		var contentLength = BitConverter.ToUInt16(data.AsSpan(2, 2));
		var timeWrittenRaw = BitConverter.ToInt64(data.AsSpan(4, 8));


		var moduleNameBytes = data.AsSpan(12, moduleNameLength).ToArray();
		string moduleName = Encoding.ASCII.GetString(moduleNameBytes);


		byte contentStart = (byte) (12 + moduleNameLength);
		byte[] content = data[contentStart..(contentStart + contentLength)];

		return new(logStream, moduleName, content, timeWrittenRaw);
	}
	public static LogEntry Parse(byte[] header, byte[] content)
	{
		if (header.Length < 13) throw new ArgumentOutOfRangeException(nameof(header), "Header data is too short.");
		if (content.Length < 1) throw new ArgumentOutOfRangeException(nameof(content), "Content data is too short.");

		var logStream = header[0];
		var moduleNameLength = header[1];
		var contentLength = BitConverter.ToUInt16(header.AsSpan(2, 2));
		var timeWrittenRaw = BitConverter.ToInt64(header.AsSpan(4, 8));


		var moduleNameBytes = header.AsSpan(12, moduleNameLength).ToArray();
		string moduleName = Encoding.ASCII.GetString(moduleNameBytes);

		return content.Length != contentLength ? throw new ArgumentException("Content length does not match header.")
			: new(logStream, moduleName, content, timeWrittenRaw);
	}
	public static LogEntry Parse(string serializedLog)
	{
		var fields = serializedLog.Split('|').Select(field => field.Trim()).ToArray();
		if (fields.Length != 4) throw new ArgumentException("Serialized log is not in the correct format.");

		var timeWritten = DateTime.Parse(fields[0]);
		var logStream = fields[1] switch
		{
			"REQUEST" => LogStream.Request,
			"FATAL" => LogStream.Fatal,
			"ERROR" => LogStream.Error,
			"WARNING" => LogStream.Warning,
			"OK" => LogStream.Ok,
			"INFO" => LogStream.Info,
			"NOTICE" => LogStream.Notice,
			"VERBOSE" => LogStream.Verbose,
			_ => LogStream.None
		};
		var moduleName = fields[2];
		var content = fields[3];


		return new(logStream, moduleName, content, timeWritten);
	}

	public byte[] Serialize()
	{
		var buffer = new Span<byte>(new byte[SerializedLength])
		{
			[0] = (byte) LogStream,
			[1] = (byte) _moduleNameBytes.Length
		};
		BitConverter.TryWriteBytes(buffer.Slice(2, 2), ContentLength);
		BitConverter.TryWriteBytes(buffer.Slice(4, 8), TimeWrittenBinary);

		_moduleNameBytes.CopyTo(buffer[12..]);
		var moduleLen = _moduleNameBytes.Length;

		ContentBytes.CopyTo(buffer[(12 + moduleLen)..]);

		return buffer.ToArray();
	}

	public Dictionary<string, dynamic> ToDictionary() => new()
	{
		{ "TimeWritten", TimeWritten },
		{ "LogStream", LogStream },
		{ "ModuleName", ModuleName },
		{ "Content", Content }
	};

	public static explicit operator byte[](LogEntry logEntry) => logEntry.Serialize();
	public static explicit operator LogEntry(byte[] bytes) => Parse(bytes);

	/// <summary>
	/// Convert this LogEntry to a string with the following format:<br/>
	/// <c>TimeWritten | LogStream | ModuleName | Content</c>
	/// </summary>
	/// <remarks>
	///	TimeWritten is converted to LocalTime.
	/// </remarks>
	public override string ToString()
	{
		return $"{TimeWritten.ToLocalTime(),-15:h:mm:ss tt zz} | {LogStream.ToString().ToUpperInvariant(),-7} | {ModuleName,-MaxModuleNameLength} | {Content}";
	}
}

public enum LogStream : byte
{
	/// <summary>
	/// Entries to this are discarded. This is used as the verbosity level if the user requests Sofa to be quiet.
	/// </summary>
	/// <remarks>
	///	Output is stylized as no output.
	/// </remarks>
	None,
	/// <summary>
	/// For unrecoverable errors. E.g., Port binding failure.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.Black"/> and background color of <see cref="ConsoleColor.Red"/>.
	/// </remarks>
	Fatal,
	/// <summary>
	/// For recoverable errors. E.g., GPU script failure.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.Red"/>.
	/// </remarks>
	Error,
	/// <summary>
	/// For recoverable errors caused by malformed user input. E.g., invalid IP address environment variable.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.Yellow"/>.
	/// </remarks>
	Warning,
	/// <summary>
	/// For successful actions. E.g., API startup success.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.Green"/>.
	/// </remarks>
	Ok,
	/// <summary>
	/// For general information. E.g., Bound URLs at startup.
	/// </summary>
	/// <remarks>
	///	Output is not stylized.
	/// </remarks>
	Info,
	/// <summary>
	/// For nice-to-know information. E.g., Loaded configuraion file.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.Gray"/>.
	/// </remarks>
	Notice,
	/// <summary>
	/// For detailed debugging information. E.g., GPU script output.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.DarkGray"/>.
	/// </remarks>
	Verbose,
	/// <summary>
	/// For logging every HTTP request Sofa receives. Disabled by default.
	/// </summary>
	/// <remarks>
	///	Output stylized with a foreground color of <see cref="ConsoleColor.DarkGray"/>.
	/// </remarks>
	Request
}

public static class Logs
{
	static Logs()
	{
		StreamWrittenTo += EchoLogs;
		LogBook.Write(new LogEntry(LogStream.Verbose, "Logging", "Registered logging callback."));
	}
	public static event EventHandler<LogEntry>? StreamWrittenTo;

	public static class LogBook
	{
		static LogBook()
		{
			// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
			if (SofaPaths.SubPaths.PathToLogFolder is null || !Directory.Exists(SofaPaths.SubPaths.PathToLogFolder)) return;

			CreateNewLogFile();
			// To make it easier to skim through logs, make it clear when a new instance is started.
			_logWriter?.WriteLine("================|=========|==================================|====== New Sofa instance ==================================");
		}

		internal static readonly Lock BookWriteLock = new();
		private static readonly Queue<LogEntry> SetupQueue = new();
		private static DateOnly _lastDateOpened = DateOnly.FromDateTime(DateTime.Now);
		private static StreamWriter? _logWriter;

		public static void Write(LogEntry entry)
		{
			if (entry.LogStream == LogStream.None) return;
			StreamWrittenTo?.Invoke(null, entry);
			if (entry.LogStream > ApiConfig.ApiConfiguration.LogVerbosity && entry.LogStream > ApiConfig.TerminalVerbosity) return;

			// Buffer log entries up until the `ApiConfig.ApiConfiguration` class is fully initialized.
			// Then, write them out in order.
			lock (BookWriteLock)
			{
				if (_logWriter is null)
				{
					SetupQueue.Enqueue(entry);
					return;
				}
				if (SetupQueue.Count > 0)
				{
					while (SetupQueue.TryDequeue(out var queuedEntry))
					{
						if (ApiConfig.ApiConfiguration.LogVerbosity >= queuedEntry.LogStream)
						{
							_logWriter.WriteLine(queuedEntry);
						}
					}
					_logWriter.Flush();
				}
			}

			if (ApiConfig.ApiConfiguration.LogVerbosity < entry.LogStream) return;

			CheckFileDate();

			lock (BookWriteLock)
			{
				_logWriter.WriteLine(entry);
				_logWriter.Flush();
			}
		}
		private static void CheckFileDate()
		{
			if (_lastDateOpened == DateOnly.FromDateTime(DateTime.Now)) return;

			_lastDateOpened = DateOnly.FromDateTime(DateTime.Now);
			lock (BookWriteLock)
			{
				string newFileName =
					$"[{_lastDateOpened.ToString("O")}] Sofa.log";
				_logWriter!.WriteLine(new LogEntry(LogStream.Notice, "Logging", $"System date changed. New logs will be written in '{newFileName}'"));
				_logWriter.Flush();
				_logWriter.Dispose();

				CreateNewLogFile();
			}
		}

		private static void CreateNewLogFile()
		{
			string fullPath = Path.Combine(SofaPaths.SubPaths.PathToLogFolder, $"[{_lastDateOpened.ToString("O")}] Sofa.log");

			var fileIsNew = !File.Exists(fullPath);
			_logWriter = new(fullPath, true, Encoding.UTF8);
			if (fileIsNew) _logWriter.WriteLine("Time Written    | Type    | Module Name                      | Content"); // Header
			_logWriter.Flush();
		}

		internal static void Dispose()
		{
			lock (BookWriteLock)
			{
				_logWriter?.Flush();
				_logWriter?.Dispose();
			}
		}
	}

	private static readonly Lock LogEchoLock = new();
	public static void EchoLogs(object? e, LogEntry logEntry)
	{
		if (ApiConfig.TerminalVerbosity < logEntry.LogStream) return;

		lock (LogEchoLock)
		{
			switch (logEntry.LogStream)
			{
				case LogStream.Fatal:
				{
					Console.ForegroundColor = ConsoleColor.Black;
					Console.BackgroundColor = ConsoleColor.DarkRed;
					break;
				}
				case LogStream.Error:
				{
					Console.ForegroundColor = ConsoleColor.Red;
					break;
				}
				case LogStream.Warning:
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					break;
				}
				case LogStream.Notice:
				{
					Console.ForegroundColor = ConsoleColor.Gray;
					break;
				}
				case LogStream.Ok:
				{
					Console.ForegroundColor = ConsoleColor.Green;
					break;
				}
				case LogStream.Verbose:
				{
					Console.ForegroundColor = ConsoleColor.DarkGray;
					break;
				}

				default:
				case LogStream.Info:
				case LogStream.None:
					break;
			}
			Console.WriteLine(logEntry);
			Console.ResetColor();
		}
	}
}