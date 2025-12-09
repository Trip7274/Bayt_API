using System.Text;

namespace Bayt_API;

public sealed class LogEntry
{
	// Header length: 13-38 bytes
	// Serialized Format:
	// 1 byte: Stream ID
	// 2 bytes: Content Length
	// 8 bytes: Time Written
	// (1-26) bytes: Module Name
	// STX (Start of Text) byte
	// {Content Length} bytes: Content (1-1024 bytes/chars)
	private const ushort MaxContentLength = 1024;
	private const byte MaxModuleNameLength = 26;

	public LogEntry(StreamId streamId, string moduleName, string content, DateTime? timeWritten = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Log module name or content cannot be empty or whitespace.");

		StreamId = streamId;
		ModuleName = moduleName;
		Content = content;
		if (timeWritten is not null) TimeWritten = timeWritten.Value;
	}
	public LogEntry(byte streamIdByte, string moduleName, byte[] contentBytes, long? timeWrittenBinary = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || contentBytes.Length == 0) throw new ArgumentException("Log module name or content cannot be empty or whitespace.");

		StreamId = (StreamId) streamIdByte;
		ModuleName = moduleName;
		ContentBytes = contentBytes;
		if (timeWrittenBinary is not null) TimeWritten = DateTime.FromBinary(timeWrittenBinary.Value);
	}

	public StreamId StreamId
	{
		get => (StreamId) StreamIdByte;
		private init => StreamIdByte = (byte) value;
	}
	public byte StreamIdByte { get; private init; }

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

			var valueStringBuilder = new StringBuilder();

			foreach (var valueChar in value.Where(valueChar => char.IsAscii(valueChar) && !char.IsControl(valueChar)))
			{
				valueStringBuilder.Append(valueChar);
			}
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Module name cannot be empty or whitespace.");

			_moduleNameBytes = Encoding.ASCII.GetBytes(valueStringBuilder.ToString());
		}
	}
	private readonly byte[] _moduleNameBytes = new byte[MaxModuleNameLength];

	public string Content
	{
		get => Encoding.ASCII.GetString(ContentBytes);
		private init
		{
			if (value.Length > MaxContentLength) value = value[..MaxContentLength];

			var valueStringBuilder = new StringBuilder();
			foreach (var valueChar in value.Where(valueChar => char.IsAscii(valueChar) && !char.IsControl(valueChar)))
			{
				valueStringBuilder.Append(valueChar);
			}
			if (string.IsNullOrWhiteSpace(valueStringBuilder.ToString())) throw new ArgumentException("Content cannot be null or whitespace.");

			ContentBytes = Encoding.ASCII.GetBytes(valueStringBuilder.ToString());
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
		var headerModuleNameLength = (byte) Math.Min(data.Length - 11, MaxModuleNameLength);
		if (!data.AsSpan(11, headerModuleNameLength).Contains((byte) 2)) throw new ArgumentException("Header does not contain STX (Start of Text) byte. Looks like an invalid log entry.");

		var streamId = data[0];
		var contentLength = BitConverter.ToUInt16(data.AsSpan(1, 2));
		var timeWrittenRaw = BitConverter.ToInt64(data.AsSpan(3, 8));


		// Scan for the end of the module name. (STX byte)
		byte stxOffset = 0;
		var moduleNamePotential = data.AsSpan(11, headerModuleNameLength);
		for (byte i = 0; i <= headerModuleNameLength; i++)
		{
			if (moduleNamePotential[i] == 2) break;
			stxOffset++;
		}

		var moduleNameBytes = data.AsSpan(11, stxOffset).ToArray();
		string moduleName = Encoding.ASCII.GetString(moduleNameBytes);


		// 11 is the start of the module name, you add the length of the module name (stxOffset), then add 1 for the STX byte.
		byte contentStart = (byte) (11 + stxOffset + 1);
		byte[] content = data[contentStart..(contentStart + contentLength)];

		return new(streamId, moduleName, content, timeWrittenRaw);
	}
	public static LogEntry Parse(byte[] header, byte[] content)
	{
		if (header.Length < 13) throw new ArgumentOutOfRangeException(nameof(header), "Header data is too short.");
		if (content.Length < 1) throw new ArgumentOutOfRangeException(nameof(content), "Content data is too short.");
		if (!header.AsSpan(11, Math.Min(header.Length - 11, MaxModuleNameLength)).Contains((byte) 2)) throw new ArgumentException("Header does not contain STX (Start of Text) byte. Looks like an invalid log entry.");

		var streamId = header[0];
		var contentLength = BitConverter.ToUInt16(header.AsSpan(1, 2));
		var timeWrittenRaw = BitConverter.ToInt64(header.AsSpan(3, 8));

		byte stxOffset;
		var moduleNamePotential = header.AsSpan(11, Math.Min(header.Length - 11, MaxModuleNameLength));
		for (stxOffset = 0; stxOffset <= moduleNamePotential.Length; stxOffset++)
		{
			if (moduleNamePotential[stxOffset] == 2) break;
		}

		var moduleNameBytes = header.AsSpan(11, stxOffset).ToArray();
		string moduleName = Encoding.ASCII.GetString(moduleNameBytes);

		return content.Length != contentLength ? throw new ArgumentException("Content length does not match header.")
			: new(streamId, moduleName, content, timeWrittenRaw);
	}

	public byte[] Serialize()
	{
		var buffer = new Span<byte>(new byte[SerializedLength])
		{
			[0] = StreamIdByte
		};
		BitConverter.TryWriteBytes(buffer.Slice(1, 2), ContentLength);
		BitConverter.TryWriteBytes(buffer.Slice(3, 8), TimeWrittenBinary);

		_moduleNameBytes.CopyTo(buffer[11..]);
		var moduleLen = _moduleNameBytes.Length;

		buffer[11 + moduleLen] = 2; // STX
		ContentBytes.CopyTo(buffer[(12 + moduleLen)..]);

		return buffer.ToArray();
	}

	public static implicit operator byte[](LogEntry logEntry) => logEntry.Serialize();
	public static explicit operator LogEntry(byte[] bytes) => Parse(bytes);

	/// <summary>
	/// Convert this LogEntry to a string with the following format:<br/>
	/// <c>TimeWritten | STREAM_ID | ModuleName | Content</c>
	/// </summary>
	/// <remarks>
	///	TimeWritten is converted to LocalTime.
	/// </remarks>
	public override string ToString()
	{
		return $"{TimeWritten.ToLocalTime(),-15:h:mm:ss tt zz} | {StreamId.ToString().ToUpperInvariant(),-7} | {ModuleName,-MaxModuleNameLength} | {Content}";
	}
}

public enum StreamId : byte
{
	/// <summary>
	/// Entries to this are discarded. This is used as the verbosity level if the user requests Bayt to be quiet.
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
	Verbose
}

public static class Logs
{
	static Logs()
	{
		StreamWrittenTo += EchoLogs;
		LogBook.Write(new LogEntry(StreamId.Verbose, "Logging", "Registered logging callback."));
	}
	public static event EventHandler<LogEntry>? StreamWrittenTo;

	public static class LogBook
	{
		static LogBook()
		{
			// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
			if (ApiConfig.ApiConfiguration.PathToLogFolder is null || !Directory.Exists(ApiConfig.ApiConfiguration.PathToLogFolder)) return;

			CreateNewLogFile();
		}

		private static readonly Lock BookWriteLock = new();
		private static readonly Queue<LogEntry> SetupQueue = new();
		private static DateOnly _lastDateOpened = DateOnly.FromDateTime(DateTime.Now);
		private static StreamWriter? _logWriter;

		public static void Write(LogEntry entry)
		{
			if (entry.StreamId == StreamId.None) return;
			StreamWrittenTo?.Invoke(null, entry);

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
						if (ApiConfig.ApiConfiguration.LogVerbosity >= queuedEntry.StreamIdByte)
						{
							_logWriter.WriteLine(queuedEntry);
						}
					}
					_logWriter.Flush();
				}
			}

			CheckFileDate();

			if (ApiConfig.ApiConfiguration.LogVerbosity < entry.StreamIdByte) return;

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
					$"[{_lastDateOpened.ToString("O")}] baytLog.log";
				_logWriter!.WriteLine(new LogEntry(StreamId.Notice, "Logging", $"System date changed. New logs will be written in '{newFileName}'"));
				_logWriter.Flush();
				_logWriter.Dispose();

				CreateNewLogFile();
			}
		}

		private static void CreateNewLogFile()
		{
			string fullPath = Path.Combine(ApiConfig.ApiConfiguration.PathToLogFolder, $"[{_lastDateOpened.ToString("O")}] baytLog.log");

			var fileIsNew = !File.Exists(fullPath);
			_logWriter = new(fullPath, true, Encoding.UTF8);
			if (fileIsNew) _logWriter.WriteLine("Time Written    | Type    | Module Name                | Content"); // Header
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
		if (ApiConfig.TerminalVerbosity < logEntry.StreamIdByte) return;

		lock (LogEchoLock)
		{
			switch (logEntry.StreamId)
			{
				case StreamId.Fatal:
				{
					Console.ForegroundColor = ConsoleColor.Black;
					Console.BackgroundColor = ConsoleColor.DarkRed;
					break;
				}
				case StreamId.Error:
				{
					Console.ForegroundColor = ConsoleColor.Red;
					break;
				}
				case StreamId.Warning:
				{
					Console.ForegroundColor = ConsoleColor.Yellow;
					break;
				}
				case StreamId.Notice:
				{
					Console.ForegroundColor = ConsoleColor.Gray;
					break;
				}
				case StreamId.Ok:
				{
					Console.ForegroundColor = ConsoleColor.Green;
					break;
				}
				case StreamId.Verbose:
				{
					Console.ForegroundColor = ConsoleColor.DarkGray;
					break;
				}

				default:
				case StreamId.Info:
				case StreamId.None:
					break;
			}
			Console.WriteLine(logEntry);
			Console.ResetColor();
		}
	}
}