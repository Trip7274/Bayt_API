using System.Text;
using System.Text.Json;

namespace Bayt_API;

public static class DataEndpointManagement
{
	private static string ClientDataFolder => Path.GetRelativePath(ApiConfig.BaseExecutablePath, ApiConfig.MainConfigs.ConfigProps.PathToDataFolder);

	private const string DefaultReadmeContent = """
	                                     This folder contains all server-wide data for each client, separated by client.
	                                     
	                                     Please refer to the specific client's documentation for info on the file types, along with usage details.
	                                     Make sure the server is shut down before modifying these files.
	                                     """;

	// TODO: Rework this to not use base64
	public sealed class DataFileMetadata
	{
		public DataFileMetadata(string format, string folder, string fileName, string? fileDataBase64 = null, JsonDocument? fileDataJson = null)
		{
			if (fileDataJson is null && fileDataBase64 is null)
			{
				throw new ArgumentException("Either fileDataBase64 or fileDataJson must be provided.");
			}

			fileDataBase64 ??= Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(fileDataJson));

			Format = format;
			Folder = folder;
			FileName = fileName;
			FileDataBase64 = fileDataBase64;
			FileDataJson = fileDataJson;
		}

		public string Format { init; get; }
		public string Folder { set; get; }
		public string FileName { set; get; }
		public string FileDataBase64 { init; get; }
		public JsonDocument? FileDataJson { init; get; }
	}

	private static void SanitizeFileMetadata(ref DataFileMetadata metadata)
	{
		metadata.Folder = metadata.Folder.Trim('/'); // E.g. "/Test/"
		metadata.FileName = metadata.FileName.Trim('/'); // E.g. "config.json/"
		if (metadata.Folder.Contains('/'))
		{
			metadata.Folder = metadata.Folder[..metadata.Folder.IndexOf('/')]; // E.g. "Test/subFolder". Bayt API does not support subfolders at this stage. Proper endpoints for this will be implemented later.
		}
	}

	private static void SanitizeFileMetadata(ref string folder, ref string fileName)
	{
		folder = folder.Trim('/'); // E.g. "/Test/"
		if (folder.Contains('/'))
		{
			folder = folder[..folder.IndexOf('/')]; // E.g. "Test/subFolder". Bayt API does not support subfolders at this stage.
                                           // Proper endpoints for this will be implemented later.
		}

		if (fileName.Length == 0) return;

		fileName = fileName.Trim('/'); // E.g. "config.json/"
	}

	public sealed record FileMetadata
	{
		public FileMetadata(string filePath)
		{
			FileName = Path.GetFileName(filePath);
			AbsolutePath = Path.GetFullPath(filePath);
			LastWriteTime = File.GetLastWriteTime(filePath);
			FileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		}

		public string FileName { get; }
		public string AbsolutePath { get; }
		public DateTime LastWriteTime { get; }
		public Stream FileStream { get; }
	}

	public static FileMetadata GetDataFile(string folder, string fileName)
	{
		SanitizeFileMetadata(ref folder, ref fileName);


		SetupDataFolder(folder, true);
		string folderPath = Path.Combine(ClientDataFolder, folder);
		if (fileName[0] == '.')
		{
			// Make sure the file wouldn't be hidden
			fileName = fileName[1..];
		}

		string foundFile;
		try
		{
			foundFile = Directory.GetFiles(folderPath, fileName).First();
		}
		catch (InvalidOperationException)
		{
			throw new FileNotFoundException($"File '{fileName}' was not found in the folder '{folder}'");
		}

		return new FileMetadata(foundFile);
	}



	public static async Task SetDataFile(DataFileMetadata dataObject)
	{
		SanitizeFileMetadata(ref dataObject);

		SetupDataFolder(dataObject.Folder);
		if (dataObject.FileName[0] == '.')
		{
			// Make sure the file wouldn't be hidden
			dataObject.FileName = dataObject.FileName[1..];
		}

		string filePath = Path.Combine(ClientDataFolder, dataObject.Folder, dataObject.FileName);

		await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

		if (dataObject.FileDataJson is not null)
		{
			await fileStream.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(dataObject.FileDataJson));
		}
		else
		{
			await fileStream.WriteAsync(Convert.FromBase64String(dataObject.FileDataBase64));
		}
	}

	public static void DeleteDataFiles(Dictionary<string, string> dataFiles)
	{

		foreach (var dataFile in dataFiles)
		{
			string folder = dataFile.Key;
			string fileName = dataFile.Value;
			SanitizeFileMetadata(ref folder, ref fileName);

			try
			{
				SetupDataFolder(folder, true);
			}
			catch (FileNotFoundException)
			{
				continue;
			}

			string filePath = Path.Combine(ClientDataFolder, folder, fileName);
			if (File.Exists(filePath))
			{
				File.Delete(filePath);
			}
		}
	}

	public static void DeleteDataFolder(string folder)
	{
		var emptyString = "";
		SanitizeFileMetadata(ref folder, ref emptyString);

		SetupDataFolder(folder, true);

		Directory.Delete(Path.Combine(ClientDataFolder, folder), true);
	}

	private static void SetupDataFolder(string? specificConfigFolderName = null, bool throwIfSpecificFolderDoesNotExist = false)
	{
		if (!Directory.Exists(ClientDataFolder))
		{
			Directory.CreateDirectory(ClientDataFolder);

			File.WriteAllText(Path.Combine(ClientDataFolder, "README.txt"), DefaultReadmeContent, Encoding.UTF8);
		}

		if (specificConfigFolderName == null) return;

		string folderPath = Path.Combine(ClientDataFolder, specificConfigFolderName);
		if (throwIfSpecificFolderDoesNotExist && !Directory.Exists(folderPath))
		{
			throw new FileNotFoundException($"The folder '{specificConfigFolderName}' does not exist.");
		}

		Directory.CreateDirectory(folderPath);
	}


}
