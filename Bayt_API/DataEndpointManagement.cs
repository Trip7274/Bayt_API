using System.Text;

namespace Bayt_API;

/// <summary>
/// Contains all the methods and classes to retrieve or set client data. Primarily used by the clientData endpoints.
/// </summary>
public static class DataEndpointManagement
{
	/// <summary>
	/// Relative path from the base executable to the client data folder
	/// </summary>
	private static string ClientDataFolder => ApiConfig.ApiConfiguration.PathToDataFolder;

	/// <summary>
	/// Represents metadata for a data file, including its folder location, file name, and file data.
	/// Provides utilities for accessing the file's absolute path, last modification time, and file stream.
	/// </summary>
	public sealed class DataFileMetadata
	{
		/// <summary>
		/// Constructor for <see cref="DataFileMetadata"/>
		/// </summary>
		/// <param name="folder">The name of the folder inside the root clientData folder. Subfolders will be trimmed off.</param>
		/// <param name="fileName">The name of the file itself. Extension included.</param>
		/// <param name="fileData">The contents of the file. The <see cref="FileStreamRead"/> property will fall back to creating a new stream if this is null.</param>
		/// <exception cref="ArgumentException">The provided folder and filename were either invalid or empty.</exception>
		public DataFileMetadata(string folder, string fileName, byte[]? fileData = null)
		{
			SanitizeFileFolderNames(ref folder, ref fileName);

			Folder = folder;
			FileName = fileName;
			FileData = fileData;
		}
		/// <summary>
		/// Relative path from <see cref="DataEndpointManagement.ClientDataFolder"/> to the folder containing this file.
		/// </summary>
		public string Folder { set; get; }
		/// <summary>
		/// Full file name, including the extension.
		/// </summary>
		public string FileName { set; get; }
		/// <summary>
		/// Raw contents of the file. If left null, <see cref="FileStreamRead"/> will create a new <see cref="FileStream"/> based off the file on-disk
		/// </summary>
		public byte[]? FileData { get; }

		/// <summary>
		/// Gets the absolute path to the file.
		/// </summary>
		public string AbsolutePath => Path.Combine(ClientDataFolder, Folder, FileName);

		/// <summary>
		/// Gets the date and time, in local time, that the file was last accessed at.
		/// </summary>
		public DateTime LastWriteTime => File.GetLastWriteTime(AbsolutePath);
		/// <summary>
		/// Gets the <see cref="Stream"/> for the file's data. Backed by RAM if <see cref="FileData"/> is set, or backed by physical storage otherwise.
		/// </summary>
		public Stream FileStreamRead => FileData is null ? new FileStream(AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read) : new MemoryStream(FileData);
	}

	/// <summary>
	/// Remove trailing and leading slashes, truncate subfolders from folders, and ensure a valid name for both the strings.
	/// </summary>
	/// <param name="folder">Reference to a string of the folder name. Will be edited in-place</param>
	/// <param name="fileName">Reference to a string of the file name. Will be edited in-place</param>
	/// <exception cref="ArgumentException">The provided folder or file is not valid.</exception>
	private static void SanitizeFileFolderNames(ref string folder, ref string fileName)
	{
		folder = folder.Trim(Path.DirectorySeparatorChar); // E.g. "/Test/" => "Test"
		fileName = fileName.Trim(Path.DirectorySeparatorChar); // E.g. "config.json/" => "config.json

		// Prevent potential directory traversal attacks
		while (folder.StartsWith("../"))
		{
			folder = folder[3..]; // E.g. "../Test" => "Test"
		}
		while (fileName.StartsWith("../"))
		{
			fileName = fileName[3..]; // E.g. "../Test" => "Test"
		}

		if (folder.Contains(Path.DirectorySeparatorChar))
		{
			folder = folder[..folder.IndexOf(Path.DirectorySeparatorChar)]; // E.g. "Test/subFolder" => "Test".
			// Bayt API does not support subfolders at this stage.
			// Proper endpoints for this will be implemented later.
		}

		if (string.IsNullOrWhiteSpace(fileName) || Path.GetInvalidFileNameChars().Any(fileName.Contains)
		       || string.IsNullOrWhiteSpace(folder) || Path.GetInvalidPathChars().Any(folder.Contains))
		{
			throw new ArgumentException("Folder and file name must not be empty or invalid.");
		}
	}

	/// <summary>
	/// Fetch a <see cref="DataFileMetadata"/> object of a specific data file.
	/// </summary>
	/// <param name="folder">The folder under which the file resides. Relative to the <see cref="ClientDataFolder"/> root.</param>
	/// <param name="fileName">The file's name, extension included.</param>
	/// <returns><see cref="DataFileMetadata"/> of the requested file.</returns>
	/// <exception cref="FileNotFoundException">The specified file was not found under the folder provided.</exception>
	/// <exception cref="ArgumentException">The provided folder and filename were either invalid or empty.</exception>
	public static DataFileMetadata GetDataFile(string folder, string fileName)
	{
		SanitizeFileFolderNames(ref folder, ref fileName);

		SetupDataFolder(folder, true);
		string folderPath = Path.Combine(ClientDataFolder, folder);

		string foundFile;
		try
		{
			foundFile = Directory.GetFiles(folderPath, fileName).First();
		}
		catch (InvalidOperationException)
		{
			throw new FileNotFoundException($"File '{fileName}' was not found in the folder '{folder}'");
		}

		return new DataFileMetadata(folder, fileName, File.ReadAllBytes(foundFile));
	}

	/// <summary>
	/// Flush the <see cref="DataFileMetadata.FileData"/> contents to the file represented by the object.
	/// </summary>
	/// <param name="dataObject">The <see cref="DataFileMetadata"/> object to flush to disk</param>
	/// <exception cref="ArgumentNullException">Thrown if the <see cref="DataFileMetadata.FileData"/> property is null.</exception>
	public static async Task SetDataFile(DataFileMetadata dataObject)
	{
		ArgumentNullException.ThrowIfNull(dataObject.FileData);
		SetupDataFolder(dataObject.Folder);

		string filePath = dataObject.AbsolutePath;

		await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
		await fileStream.WriteAsync(dataObject.FileData);
	}

	/// <summary>
	/// Delete the data file at the specified folder path and filename.
	/// </summary>
	/// <param name="folderName">Folder path relative to the <see cref="ClientDataFolder"/> root.</param>
	/// <param name="fileName">Name of the file to delete.</param>
	/// <exception cref="DirectoryNotFoundException">The parent folder doesn't exist.</exception>
	/// <exception cref="ArgumentException">The provided folder and filename were either invalid or empty.</exception>
	public static void DeleteDataFile(string folderName, string fileName)
	{
		SanitizeFileFolderNames(ref folderName, ref fileName);

		SetupDataFolder(folderName,true);

		string filePath = Path.Combine(ClientDataFolder, folderName, fileName);
		if (File.Exists(filePath))
		{
			File.Delete(filePath);
		}
	}

	/// <summary>
	/// Deletes an entire data folder.
	/// </summary>
	/// <param name="folder">Folder to delete. Relative to the <see cref="ClientDataFolder"/> root.</param>
	/// <exception cref="DirectoryNotFoundException">The specified directory was not found.</exception>
	/// <exception cref="ArgumentException">The provided folder and filename were either invalid or empty.</exception>
	public static void DeleteDataFolder(string folder)
	{
		var emptyString = "this is just to satisfy the method. It is not used.";
		SanitizeFileFolderNames(ref folder, ref emptyString);

		SetupDataFolder(folder, true);

		Directory.Delete(Path.Combine(ClientDataFolder, folder), true);
	}

	/// <summary>
	/// Ensure the root <see cref="ClientDataFolder"/> and the specified data folder both exist. Optionally throw an exception if the latter check failed.
	/// </summary>
	/// <param name="dataFolderName">The specific data folder's name. If left null, the second check will be skipped.</param>
	/// <param name="throwIfSpecificFolderDoesNotExist">Whether to throw an exception if the specific folder didn't already exist or create it.</param>
	/// <exception cref="DirectoryNotFoundException">Only thrown if <see cref="throwIfSpecificFolderDoesNotExist"/> was set to true. Prevents the creation of the folder.</exception>
	private static void SetupDataFolder(string? dataFolderName = null, bool throwIfSpecificFolderDoesNotExist = false)
	{
		if (dataFolderName == null) return;

		string folderPath = Path.Combine(ClientDataFolder, dataFolderName);
		if (throwIfSpecificFolderDoesNotExist && !Directory.Exists(folderPath))
		{
			throw new DirectoryNotFoundException($"The folder '{dataFolderName}' does not exist.");
		}

		if (!Directory.Exists(folderPath))
		{
			Directory.CreateDirectory(folderPath);
		}
	}
}