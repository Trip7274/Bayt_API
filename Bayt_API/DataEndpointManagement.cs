namespace Bayt_API;

/// <summary>
/// Contains all the methods and classes to retrieve or set client data. Primarily used by the clientData endpoints.
/// </summary>
public static class DataEndpointManagement
{
	/// <summary>
	/// Abs. path to the client data folder.
	/// </summary>
	private static string ClientDataFolder => ApiConfig.ApiConfiguration.PathToDataFolder;

	/// <summary>
	/// Represents metadata for a data file, including its folder location, file name, and file data.
	/// Provides utilities for accessing the file's absolute path, last modification time, and file stream.
	/// </summary>
	public sealed record DataFileMetadata
	{
		/// <summary>
		/// Constructor for <see cref="DataFileMetadata"/>
		/// </summary>
		/// <param name="folder">The name of the folder inside the root clientData folder. Subfolders will be trimmed off.</param>
		/// <param name="fileName">The name of the file itself. Extension included.</param>
		/// <param name="fileData">The contents of the file. The <see cref="ReadStream"/> property will fall back to creating a new stream if this is null.</param>
		/// <param name="createMissing">Whether to create a new folder and/or file if the specified folder/file didn't exist in the clientData root, or throw a <see cref="DirectoryNotFoundException"/></param>
		/// <exception cref="ArgumentException">The provided folder and filename were either invalid or empty.</exception>
		/// <exception cref="DirectoryNotFoundException">The specified directory was not found, and <c>createMissing</c> was set to false.</exception>
		/// <exception cref="FileNotFoundException">The specified file was not found at the specified folder, and no data was passed to create one.</exception>
		public DataFileMetadata(string? folder, string? fileName, byte[]? fileData = null, bool createMissing = false)
		{
			var pathCheck = EnsureSafePaths(folder, fileName);
			if (pathCheck is not null) throw new ArgumentException(pathCheck);

			SetupDataFolder(folder!, !createMissing);
			if (!File.Exists(Path.Combine(ClientDataFolder, folder!, fileName!)) && fileData is null && !createMissing)
				throw new FileNotFoundException($"The file '{fileName}' does not exist.");

			FolderName = folder!;
			FileName = fileName!;
			if (fileData is not null) FileData = fileData;
		}

		public void DeleteFile()
		{
			if (!File.Exists(AbsolutePath)) return;
			try
			{
				File.Delete(AbsolutePath);
			}
			catch (IOException)
			{
				Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
					$"The '{Path.GetFileName(AbsolutePath)}' clientData file is in use, thus it was not deleted."));
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
					$"The current user does not have the permission to delete the '{Path.GetFileName(AbsolutePath)}' clientData file, or it is read-only, thus it was not deleted."));
				throw;
			}
		}

		/// <summary>
		/// Relative path from <see cref="DataEndpointManagement.ClientDataFolder"/> to the folder containing this file.
		/// </summary>
		public string FolderName { get; }
		/// <summary>
		/// Full file name, including the extension.
		/// </summary>
		public string FileName { get; }
		/// <summary>
		/// Gets the absolute path to the file.
		/// </summary>
		public string AbsolutePath => Path.GetFullPath(Path.Combine(ClientDataFolder, FolderName, FileName));

		/// <summary>
		/// Get: Gets the file's contents. Will be null if the file doesn't exist.<br/>
		/// Set: Sets the file's contents. Will empty the file's contents if set to null.
		/// </summary>
		public byte[]? FileData
		{
			get => File.Exists(AbsolutePath) ? File.ReadAllBytes(AbsolutePath) : null;
			set => File.WriteAllBytes(AbsolutePath, value ?? []);
		}

		/// <summary>
		/// Gets the date and time, in local time, that the file was last accessed at.
		/// </summary>
		public DateTime LastWriteTime => File.GetLastWriteTime(AbsolutePath);
		/// <summary>
		/// Gets the <see cref="FileStream"/> for the file with read access. Will return null if the file doesn't exist.
		/// </summary>
		public FileStream? ReadStream => File.Exists(AbsolutePath) ? new FileStream(AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
	}

	/// <summary>
	/// Deletes the specified sub-folder, after making sure it's within the <see cref="ClientDataFolder"/>.
	/// </summary>
	/// <param name="folderPath">The path to the specific clientData subfolder (relative to the clientData root <see cref="ClientDataFolder"/>).</param>
	/// <exception cref="ArgumentException">The path safety check failed. The path is probably invalid or unsafe.</exception>
	/// <exception cref="DirectoryNotFoundException">The specified subfolder does not exist under the root clientData folder.</exception>
	/// <exception cref="UnauthorizedAccessException">The user does not have write access to the specified folder.</exception>
	/// <exception cref="IOException">The folder is either in use, read-only, or contains a read-only file.</exception>
	public static void DeleteDataFolder(string? folderPath)
	{
		var pathCheck = EnsureSafeFolderPath(folderPath);
		if (pathCheck is not null) throw new ArgumentException(pathCheck);

		folderPath = Path.GetFullPath(Path.Combine(ClientDataFolder, folderPath!));

		if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException($"Path does not exist. ({folderPath})");

		try
		{
			Directory.Delete(folderPath, true);
		}
		catch (UnauthorizedAccessException)
		{
			Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
				"The permissions for the clientData folder seem incorrect. (No write permission) Please ensure your user has write access to it."));
			throw;
		}
		catch (IOException)
		{
			Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
				$"The '{Path.GetFileName(folderPath)}' clientData folder is either in use, read-only, or contains a read-only file, thus it was not deleted."));
			throw;
		}
	}

	/// <summary>
	/// Ensure the folder + file names are safe and within the <see cref="ClientDataFolder"/>.
	/// </summary>
	/// <param name="folder">The relative path to the folder from the clientData root.</param>
	/// <param name="fileName">The file's name</param>
	/// <exception cref="ArgumentException">The provided folder and/or file names are not valid, or are not under the clientData root.</exception>
	/// <returns>
	///	Null if the folder and file names are valid. Otherwise, a string containing the error message.
	/// </returns>
	private static string? EnsureSafePaths(string? folder, string? fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName) || Path.GetInvalidFileNameChars().Any(fileName.Contains)
		   || string.IsNullOrWhiteSpace(folder) || Path.GetInvalidPathChars().Any(folder.Contains))
		{
			Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
				$"Invalid folder or file name: '{folder} + {fileName}'"));
			return "Folder and file name must not be empty or contain invalid characters.";
		}


		// Directory traversal protection
		if (Path.GetFullPath(Path.Combine(ClientDataFolder, folder, fileName)).StartsWith(ClientDataFolder)) return null;

		Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
			$"Requested path is outside the clientData folder: '{Path.GetFullPath(Path.Combine(ClientDataFolder, folder, fileName))}'"));
		return "Requested path is outside the clientData folder.";
	}
	private static string? EnsureSafeFolderPath(string? folder)
	{
		if (string.IsNullOrWhiteSpace(folder) || Path.GetInvalidPathChars().Any(folder.Contains))
		{
			Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
				$"Invalid folder name: '{folder}'"));
			return "Folder name must not be empty or contain invalid characters.";
		}


		// Directory traversal protection
		if (Path.GetFullPath(Path.Combine(ClientDataFolder, folder)).StartsWith(ClientDataFolder)) return null;

		Logs.LogStream.Write(new(StreamId.Error, "Client Data Management",
			$"Requested path is outside the clientData folder: '{Path.GetFullPath(Path.Combine(ClientDataFolder, folder))}'"));
		return "Requested path is outside the clientData folder.";
	}

	/// <summary>
	/// Ensure the root <see cref="ClientDataFolder"/> and the specified data folder both exist. Optionally throw an exception if the latter check failed.
	/// </summary>
	/// <param name="dataFolderName">The specific data folder's name. Relative to <see cref="ClientDataFolder"/>.</param>
	/// <param name="throwIfSpecificFolderDoesNotExist">Whether to throw an exception if the specific folder didn't already exist or create it.</param>
	/// <exception cref="DirectoryNotFoundException">Only thrown if <see cref="throwIfSpecificFolderDoesNotExist"/> was set to true and the folder did not exist.</exception>
	/// <remarks>
	///	This method will not check if the specified path is safe or not, please use <see cref="EnsureSafePaths"/> prior to this for that.
	/// </remarks>
	private static void SetupDataFolder(string dataFolderName, bool throwIfSpecificFolderDoesNotExist = false)
	{
		string folderPath = Path.GetFullPath(Path.Combine(ClientDataFolder, dataFolderName));
		if (Directory.Exists(folderPath)) return;

		if (throwIfSpecificFolderDoesNotExist)
			throw new DirectoryNotFoundException($"The folder '{dataFolderName}' does not exist.");

		Directory.CreateDirectory(folderPath);
	}
}