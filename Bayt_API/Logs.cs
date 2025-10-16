using System.Globalization;
using System.Text;

namespace Bayt_API;

public sealed class LogEntry
{
	// Serialized Format:
	// 1 byte: Stream ID
	// 1 byte: Content Length
	// 8 bytes: Time Written
	// 32 bytes: Module Name
	// {Content Length} bytes: Content (Max 255 bytes/chars)

	public LogEntry(StreamId? streamId = null, string? moduleName = null, string? content = null, DateTime? timeWritten = null)
	{
		if (streamId is not null) StreamId = streamId.Value;
		if (moduleName is not null) ModuleName = moduleName;
		if (content is not null) Content = content;
		if (timeWritten is not null) TimeWritten = timeWritten.Value;
	}

	public StreamId StreamId
	{
		get => (StreamId) StreamIdInternal;
		set => StreamIdInternal = (byte) value;
	}

	public byte StreamIdInternal { get; set; }

	public byte ContentLength => (byte) int.Clamp(ContentBytes.Length, 0, byte.MaxValue);

	public DateTime TimeWritten
	{
		get => DateTime.FromBinary(TimeWrittenBinary);
		init => TimeWrittenBinary = value.ToBinary();
	}
	public long TimeWrittenBinary { get; init; } = DateTime.Now.ToBinary();

	public string ModuleName
	{
		get => Encoding.UTF8.GetString(_moduleNameBytes);
		init
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("Module name cannot be null or whitespace.");
			if (value.Length > 32) value = value[..32];

			_moduleNameBytes = Encoding.UTF8.GetBytes(value);
		}
	}

	private readonly byte[] _moduleNameBytes = new byte[32];

	public string Content
	{
		get => Encoding.UTF8.GetString(_contentRaw);
		set
		{
			if (value.Length > byte.MaxValue) value = value[..byte.MaxValue];
			_contentRaw = Encoding.UTF8.GetBytes(value);
		}
	}

	public byte[] ContentBytes
	{
		get => _contentRaw;
		set
		{
			if (value.Length > byte.MaxValue) value = value[..byte.MaxValue];
			_contentRaw = value;
		}
	}
	private byte[] _contentRaw = [];

	public static LogEntry Parse(byte[] data)
	{
		if (data.Length < 42) throw new ArgumentOutOfRangeException(nameof(data), "Data is too short.");

		var streamId = data[0];
		var contentLength = data[1];
		var timeWrittenRaw = BitConverter.ToInt64(data.AsSpan(2, 8));
		var moduleName = data.AsSpan(10, 32).ToArray();
		string moduleNameString = Encoding.UTF8.GetString(moduleName).TrimEnd('\0');

		byte[] content = data[42..(42 + contentLength)];

		return new()
		{
			StreamIdInternal = streamId,
			TimeWrittenBinary = timeWrittenRaw,
			ModuleName = moduleNameString,
			ContentBytes = content
		};
	}
	public static LogEntry Parse(byte[] header, byte[] content)
	{
		if (header.Length < 42) throw new ArgumentOutOfRangeException(nameof(header), "Data is too short.");

		var streamId = header[0];
		var timeWrittenRaw = BitConverter.ToInt64(header.AsSpan(2, 8));
		var moduleName = header.AsSpan(10, 32).ToArray();

		return new()
		{
			StreamIdInternal = streamId,
			TimeWrittenBinary = timeWrittenRaw,
			ModuleName = Encoding.UTF8.GetString(moduleName).TrimEnd('\0'),
			ContentBytes = content
		};
	}

	public byte[] Serialize()
	{
		List<byte> byteList =
		[
			StreamIdInternal,
			(byte) Content.Length
		];

		byteList.AddRange(BitConverter.GetBytes(TimeWrittenBinary));

		var moduleNameBytes = _moduleNameBytes;
		if (moduleNameBytes.Length < 32) Array.Resize(ref moduleNameBytes, 32);
		byteList.AddRange(moduleNameBytes);
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
	private static readonly int MaxBytesLength = ApiConfig.ApiConfiguration.KeepMoreLogs ? ushort.MaxValue * 100 : ushort.MaxValue;
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
			var entryHeader = new byte[42];
			try
			{
				await MemoryStream.ReadExactlyAsync(entryHeader);
			}
			catch (EndOfStreamException)
			{
				return null;
			}

			byte contentLength = entryHeader[1];
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
			if (MemoryStream.Length < MaxBytesLength) return;

			MemoryStream.SetLength(MaxBytesLength);
			// TODO: This needs more testing.
		}

		private static readonly MemoryStream MemoryStream = new();
	}

	public static void EchoLogs(object? e, LogEntry logEntry)
	{
		if (ApiConfig.VerbosityLevel < logEntry.StreamIdInternal) return;

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