using System.Globalization;
using System.Text;

namespace Bayt_API;

public sealed class LogEntry
{
	// Header length: 13-44 bytes
	// Serialized Format:
	// 1 byte: Stream ID
	// 2 bytes: Content Length
	// 8 bytes: Time Written
	// (1-32) bytes: Module Name
	// STX (Start of Text) byte
	// {Content Length} bytes: Content (1-1024 bytes/chars)
	public const ushort MaxContentLength = 1024;

	public LogEntry(StreamId streamId, string moduleName, string content, DateTime? timeWritten = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Log module name and content cannot be empty or whitespace.");

		StreamId = streamId;
		ModuleName = moduleName;
		Content = content;
		if (timeWritten is not null) TimeWritten = timeWritten.Value;
	}
	public LogEntry(byte streamIdByte, string moduleName, byte[] contentBytes, long? timeWrittenBinary = null)
	{
		if (string.IsNullOrWhiteSpace(moduleName) || contentBytes.Length == 0) throw new ArgumentException("Log module name and content cannot be empty or whitespace.");

		StreamId = (StreamId) streamIdByte;
		ModuleName = moduleName;
		ContentBytes = contentBytes;
		if (timeWrittenBinary is not null) TimeWrittenBinary = timeWrittenBinary.Value;
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
		private init => TimeWrittenBinary = value.ToBinary();
	}
	public long TimeWrittenBinary { get; private init; } = DateTime.Now.ToBinary();

	public string ModuleName
	{
		get => Encoding.ASCII.GetString(_moduleNameBytes);
		private init
		{
			if (value.Length > 32) value = value[..32];

			var valueStringBuilder = new StringBuilder();

			foreach (var valueChar in value.Where(valueChar => char.IsAscii(valueChar) && !char.IsControl(valueChar)))
			{
				valueStringBuilder.Append(valueChar);
			}
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Module name cannot be empty or whitespace.");

			_moduleNameBytes = Encoding.ASCII.GetBytes(valueStringBuilder.ToString());
		}
	}

	private readonly byte[] _moduleNameBytes = new byte[32];

	public string Content
	{
		get => Encoding.ASCII.GetString(_contentRaw);
		private init
		{
			if (value.Length > MaxContentLength) value = value[..MaxContentLength];

			var valueStringBuilder = new StringBuilder();
			foreach (var valueChar in value.Where(valueChar => char.IsAscii(valueChar) && !char.IsControl(valueChar)))
			{
				valueStringBuilder.Append(valueChar);
			}
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Content cannot be null or whitespace.");

			_contentRaw = Encoding.ASCII.GetBytes(value);
		}
	}

	public byte[] ContentBytes
	{
		get => _contentRaw;
		private init
		{
			if (value.Length > MaxContentLength) value = value[..MaxContentLength];
			_contentRaw = value;
		}
	}
	private readonly byte[] _contentRaw = [];

	public static LogEntry Parse(byte[] data)
	{
		if (data.Length < 14) throw new ArgumentOutOfRangeException(nameof(data), "Data is too short.");
		var headerModuleNameLength = (byte) Math.Min(data.Length - 11, 32);
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
		if (!header.AsSpan(11, Math.Min(header.Length - 11, 32)).Contains((byte) 2)) throw new ArgumentException("Header does not contain STX (Start of Text) byte. Looks like an invalid log entry.");

		var streamId = header[0];
		var contentLength = BitConverter.ToUInt16(header.AsSpan(1, 2));
		var timeWrittenRaw = BitConverter.ToInt64(header.AsSpan(3, 8));

		byte stxOffset;
		var moduleNamePotential = header.AsSpan(11, Math.Min(header.Length - 11, 32));
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
		List<byte> byteList =
		[
			StreamIdByte
		];

		byteList.AddRange(BitConverter.GetBytes((ushort) Content.Length));
		byteList.AddRange(BitConverter.GetBytes(TimeWrittenBinary));

		var moduleNameBytes = _moduleNameBytes;
		byteList.AddRange(moduleNameBytes);
		byteList.Add(2);
		byteList.AddRange(ContentBytes);

		return byteList.ToArray();
	}

	public override string ToString()
	{
		// [INFO] ModuleName: Content
		return ApiConfig.ApiConfiguration.ShowTimestampsInLogs ?
			$"{TimeWritten.ToLongTimeString()} - [{StreamId.ToString().ToUpperInvariant()}] {ModuleName}: {Content}"
			: $"[{StreamId.ToString().ToUpperInvariant()}] {ModuleName}: {Content}";
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
	private static readonly Lock LogLock = new();
	private static readonly int MaxStreamLength = ApiConfig.ApiConfiguration.KeepMoreLogs ? ushort.MaxValue * 100 : ushort.MaxValue;
	public static event EventHandler<LogEntry>? StreamWrittenTo;

	public static class LogStream
	{
		public static async ValueTask WriteAsync(LogEntry entry) {
			if (entry.StreamId == StreamId.None) return;
			ReadOnlyMemory<byte> textBytes = entry.Serialize();

			StreamWrittenTo?.Invoke(null, entry);
			await MemoryStream.WriteAsync(textBytes);
			Truncate();
		}

		public static void Write(LogEntry entry)
		{
			if (entry.StreamId == StreamId.None) return;
			ReadOnlySpan<byte> entryBytes = entry.Serialize();

			StreamWrittenTo?.Invoke(null, entry);
			MemoryStream.Write(entryBytes);
			Truncate();
		}

		public static async Task<List<LogEntry>> ReadAll()
		{
			var oldPos = MemoryStream.Position;
			MemoryStream.Position = 0;
			var entries = new List<LogEntry>();
			while (true)
			{
				var entry = await ReadEntry();
				if (entry is null) break;
				entries.Add(entry);
			}

			MemoryStream.Position = oldPos;
			return entries;
		}

		public static async Task<LogEntry?> ReadEntry()
		{
			MemoryStream.Position = 0;
			var entryHeader = new byte[44];
			try
			{
				await MemoryStream.ReadExactlyAsync(entryHeader);
			}
			catch (EndOfStreamException)
			{
				return null;
			}

			ushort contentLength = ushort.Clamp(BitConverter.ToUInt16(entryHeader.AsSpan(1, 2)), 0, LogEntry.MaxContentLength);
			byte[] content = new byte[contentLength];
			await MemoryStream.ReadExactlyAsync(content);

			return LogEntry.Parse(entryHeader, content);
		}
		public static MemoryStream ReadStream()
		{
			return MemoryStream;
		}

		public static void Clear() =>
			MemoryStream.SetLength(0);

		public static void Truncate()
		{
			if (MemoryStream.Length < MaxStreamLength) return;

			MemoryStream.SetLength(MaxStreamLength);
			// TODO: This needs more testing.
		}

		private static readonly MemoryStream MemoryStream = new();
	}

	public static void EchoLogs(object? e, LogEntry logEntry)
	{
		if (ApiConfig.VerbosityLevel < logEntry.StreamIdByte) return;

		lock (LogLock)
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
			Console.WriteLine(logEntry.ToString());
			Console.ResetColor();
		}
	}
}