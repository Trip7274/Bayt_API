using System.Text.Json;

namespace Sofa_API;

public static class SofaPaths
{
	static SofaPaths()
	{
		if (!Directory.Exists(BaseConfigPath))
		{
			Directory.CreateDirectory(BaseConfigPath);
			File.WriteAllText(Path.Combine(BaseConfigPath, "README"), "This folder is where the configuration for Sofa is stored.\n" +
			                                                          "More info: https://github.com/Trip7274/Sofa");
		}
		if (!Directory.Exists(BaseDataPath))
		{
			Directory.CreateDirectory(BaseDataPath);
		}
	}
	/// <summary>
	/// Abs. path to the Sofa binary's directory
	/// </summary>
	public static readonly string BaseExecutablePath = AppContext.BaseDirectory;

	/// <summary>
	/// Abs. path to the configuration directory. E.g. <c>/home/{user}/.config/Sofa/</c>.
	/// </summary>
	/// <remarks>
	///	Tries to fetch the env var <c>SOFA_CONFIG_DIRECTORY</c> first, then <c>XDG_CONFIG_HOME</c>. Falls back to "<see cref="BaseExecutablePath"/>/Sofa" if neither are set.
	/// </remarks>
	public static readonly string BaseConfigPath = Path.Combine(Environment.GetEnvironmentVariable("SOFA_CONFIG_DIRECTORY") ??
	                                                            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ??
	                                                            BaseExecutablePath, "Sofa");

	/// <summary>
	/// Abs. path to the sofaData directory. Used for <see cref="BaseExecutablePath"/> and <see cref="BaseExecutablePath"/>. E.g. <c>/home/{user}/.local/share/Sofa/</c>.
	/// </summary>
	/// <remarks>
	///	Tries to fetch the env var <c>SOFA_DATA_DIRECTORY</c> first, then <c>XDG_DATA_HOME</c>. Falls back to "<see cref="BaseExecutablePath"/>/Sofa" if neither are set.
	/// </remarks>
	public static readonly string BaseDataPath = Path.Combine(Environment.GetEnvironmentVariable("SOFA_DATA_DIRECTORY") ??
	                                                          Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
	                                                          BaseExecutablePath, "Sofa");
	public static class SubPaths
	{
		static SubPaths()
		{
			if (!Directory.Exists(PathToDataFolder))
			{
				const string defaultReadmeContent = """
				                                    This folder contains all server-wide data for each client, separated by client.

				                                    Please refer to the specific client's documentation for info on the file types, along with usage details.
				                                    Make sure the server is shut down before modifying these files.
				                                    Sofa API created this folder. More info: https://github.com/Trip7274/Sofa
				                                    """;

				Directory.CreateDirectory(PathToDataFolder);
				File.WriteAllText(Path.Combine(PathToDataFolder, "README"), defaultReadmeContent);
			}
			if (!Directory.Exists(PathToDockerFolder))
			{
				const string defaultReadmeContent = """
				                                    This folder contains all the Docker-related files made and managed by Sofa API.

				                                    You should be safe manually deleting/editing these files, provided the relevant container, and Sofa, are not running.

				                                    Short description of the folders/files:
				                                    -> "Containers": Stores the folders for the Sofa-created Docker Compose containers.
				                                    -> "imageData.json": Stores the metadata for Docker images fetched by Sofa API.

				                                    Sofa API created this folder. More info: https://github.com/Trip7274/Sofa
				                                    """;

				Directory.CreateDirectory(PathToDockerFolder);
				File.WriteAllText(Path.Combine(PathToDockerFolder, "README"), defaultReadmeContent);
			}
			if (!Directory.Exists(PathToComposeFolder)) Directory.CreateDirectory(PathToComposeFolder);

			if (!File.Exists(DockerLocal.PathToImageDataFile))
			{
				Dictionary<string, string[]> defaultDict = [];
				File.WriteAllText(DockerLocal.PathToImageDataFile, JsonSerializer.Serialize(defaultDict, ApiConfig.SofaJsonSerializerOptions));
			}

			if (!Directory.Exists(PathToLogFolder)) Directory.CreateDirectory(PathToLogFolder);
		}
		/// <summary>
		/// Abs. path to the folder containing all the docker-related folders.
		/// </summary>
		/// <remarks>
		///	Is set to "<see cref="SofaPaths.BaseDataPath"/>/Docker".
		/// </remarks>
		public static readonly string PathToDockerFolder = Path.Combine(BaseDataPath, "Docker");

		/// <summary>
		/// Abs. path to the folder containing all the Docker compose folders.
		/// </summary>
		/// <remarks>
		/// Is set to "<see cref="PathToDockerFolder"/>/Containers".
		/// </remarks>
		public static readonly string PathToComposeFolder = Path.Combine(PathToDockerFolder, "Containers");

		/// <summary>
		///	Abs. path to the client data folder.
		/// </summary>
		/// <remarks>
		///	Is set to "<see cref="SofaPaths.BaseDataPath"/>/ClientData".
		/// </remarks>
		public static readonly string PathToDataFolder = Path.Combine(BaseDataPath, "ClientData");

		/// <summary>
		/// Abs. path to the folder containing all the logs.
		/// </summary>
		/// <remarks>
		///	Is set to "<see cref="SofaPaths.BaseDataPath"/>/Logs".
		/// </remarks>
		public static readonly string PathToLogFolder = Path.Combine(BaseDataPath, "Logs");
		/// <summary>
		/// Abs. path to the folder containing Sofa's certificates.
		/// </summary>
		/// <remarks>
		///	Is set to "<see cref="SofaPaths.BaseDataPath"/>/Certificates".
		/// </remarks>
		public static readonly string PathToCertificatesFolder = Path.Combine(BaseDataPath, "Certificates");

		/// <summary>
		/// Abs. path to the specific configuration loaded currently.
		/// </summary>
		public static readonly string ConfigFilePath = Path.Combine(BaseConfigPath, "ApiConfiguration.json");
	}
}