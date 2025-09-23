using System.Text;

namespace Bayt_API;

public static class LogExtensions
{

}

public sealed class LogEntry
{
	// Serialized Format:
	// 1 byte: Stream ID
	// 2 bytes: Content Length
	// 8 bytes: Time Written
	// 16 bytes: Module Name
	// {Content Length} bytes: Content

	public byte StreamId { get; init; }
	public ushort ContentLength { get; init; }

	public DateTime TimeWritten
	{
		get => DateTime.FromBinary(TimeWrittenBinary);
		init => TimeWrittenBinary = value.ToBinary();
	}
	public long TimeWrittenBinary { get; init; } = DateTime.Now.ToBinary();

	public string ModuleName
	{
		get => Encoding.UTF8.GetString(ModuleNameBytes);
		init
		{
			if (string.IsNullOrWhiteSpace(value))
				throw new ArgumentException("Module name cannot be null or whitespace.");
			if (value.Length > 16) value = value[..16];

			ModuleNameBytes = Encoding.UTF8.GetBytes(value);
		}
	}

	public readonly byte[] ModuleNameBytes = new byte[16];

	public required ReadOnlyMemory<byte> Content { get; set; }

	public static LogEntry Parse(byte[] data)
	{
		if (data.Length < 27) throw new ArgumentOutOfRangeException(nameof(data), "Data is too short.");

		var streamId = data[0];
		var contentLength = BitConverter.ToUInt16(data.AsSpan(1, 2));
		var timeWrittenRaw = BitConverter.ToInt64(data.AsSpan(3, 8));
		var moduleName = data.AsSpan(11, 16).ToArray();
		ReadOnlyMemory<byte> content = data.AsMemory(27, contentLength);

		return new()
		{
			StreamId = streamId,
			ContentLength = contentLength,
			TimeWrittenBinary = timeWrittenRaw,
			ModuleName = Encoding.UTF8.GetString(moduleName),
			Content = content
		};
	}

	public byte[] Serialize()
	{
		if (Content.Length > ushort.MaxValue) Content = Content[..ushort.MaxValue];

		List<byte> byteList =
		[
			StreamId
		];

		byteList.AddRange(BitConverter.GetBytes((ushort) Content.Length));
		byteList.AddRange(BitConverter.GetBytes(TimeWrittenBinary));
		byteList.AddRange(ModuleNameBytes);
		byteList.AddRange(Content.ToArray());

		return byteList.ToArray();
	}
}

public static class Logs
{
	private static readonly ushort MaxLinesLength = ApiConfig.ApiConfiguration.KeepMoreLogs ? ushort.MaxValue : byte.MaxValue;

	public sealed class LogStream
	{
		public static event EventHandler<StreamWriteEvent>? StreamWrittenTo;
		public class StreamWriteEvent : EventArgs
		{
			public required byte StreamId { get; init; }
			public required string ModuleName { get; init; }
			public required ReadOnlyMemory<byte> ContentWritten { get; init; }
			public DateTime TimeWritten { get; init; } = DateTime.Now;
		}

		public async ValueTask WriteAsync(string text, string moduleName) {
			if (text.Length > MaxLinesLength) text = text[..MaxLinesLength];
			if (text.Length == 0) return;
			if (text.Contains('\n')) text = text.Remove('\n');

			ReadOnlyMemory<byte> textBytes = Encoding.UTF8.GetBytes(text);

			StreamWrittenTo?.Invoke(this, new StreamWriteEvent
			{
				StreamId = StreamId,
				ModuleName = moduleName,
				ContentWritten = textBytes
			});
			await _memoryStream.WriteAsync(textBytes);
		}
		public async ValueTask WriteAsync(byte[] data, string moduleName) {
			StreamWrittenTo?.Invoke(this, new StreamWriteEvent
			{
				StreamId = StreamId,
				ModuleName = moduleName,
				ContentWritten = data
			});
			await _memoryStream.WriteAsync(data);
		}

		public void Write(string text, string moduleName)
		{
			var textBytes = Encoding.UTF8.GetBytes(text);

			StreamWrittenTo?.Invoke(this, new StreamWriteEvent
			{
				StreamId = StreamId,
				ModuleName = moduleName,
				ContentWritten = textBytes
			});
			_memoryStream.Write(textBytes);
		}
		public void Write(byte[] data, string moduleName)
		{
			StreamWrittenTo?.Invoke(this, new StreamWriteEvent
			{
				StreamId = StreamId,
				ModuleName = moduleName,
				ContentWritten = data
			});
			_memoryStream.Write(data);
		}

		public MemoryStream ReadStream()
		{
			return _memoryStream;
		}

		public void Clear() =>
			_memoryStream.SetLength(0);

		public void Truncate() =>
			_memoryStream.SetLength(MaxLinesLength); // TODO: This wouldn't work. It'd truncate the wrong end. Need to fix.

		private byte StreamId { get; init; }
		private readonly MemoryStream _memoryStream = new();
	}

	public static LogStream FatalStream { get; } = new();
	public static LogStream ErrorStream { get; } = new();
	public static LogStream WarningStream { get; } = new();
	public static LogStream InfoStream { get; } = new();
	public static LogStream VerboseStream { get; } = new();
}