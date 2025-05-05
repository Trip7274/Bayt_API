using System.Text.Json;

namespace Bayt_API;

public static class ApiConfig
{
	public const string Version = "0.5.5";
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public static DateTime LastUpdated { get; set; }
	public static readonly string BaseExecutablePath = Path.GetDirectoryName(Environment.ProcessPath) ?? throw new Exception("Could not get process path");
	private static readonly string BaseConfigPath = Path.Combine(BaseExecutablePath, "config");

	private static class ConfigPropertyTypes
	{
		internal static class MainConfigProperties
		{
			internal static readonly List<string> RequiredProperties =
			[
				"ConfigVersion",
				"WatchedMountsConfigPath"
			];

			internal static readonly List<string> OptionalProperties =
			[
				"BackendName",
				"SecondsToUpdate",
				"NetworkPort"

			];
		}
		internal static class WatchedMountsConfigProperties
		{
			internal static readonly List<string> RequiredProperties =
			[
				"WatchedMounts"
			];

			internal static readonly List<string> OptionalProperties = [];
		}
	}


	// Config management

	/// <summary>
	/// An abstract template for configuration classes. Contains methods for checking, generating, and updating various configurations.
	/// </summary>
	public abstract class ConfigTemplate
	{
		protected ConfigTemplate(string configFilePath)
		{
			if (!Directory.Exists(BaseConfigPath))
			{
				Directory.CreateDirectory(BaseConfigPath);
			}

			ConfigFilePath = configFilePath;
			JsonElm = GetConfig();
		}

		protected string ConfigFilePath { get; }

		/// <summary>
		/// Full JSON of the specific class of configuration.
		/// </summary>
		/// <remarks>
		/// Using this directly is not recommended. You should look into the specific properties of the class itself for whatever element you want.
		/// </remarks>
		public JsonElement JsonElm { get; private set; }
		protected abstract List<string> RequiredProperties { get; }

		/// <summary>
		/// Checks the corresponding in-disk configuration file for corruption or incompleteness and returns the JsonElement of it.
		/// </summary>
		/// <returns>
		/// JsonElement of the corresponding in-disk configuration file
		/// </returns>
		private JsonElement GetConfig()
		{
			CheckConfig();

			return JsonDocument.Parse(File.ReadAllText(ConfigFilePath)).RootElement;
		}

		/// <summary>
		/// Checks the in-disk configuration file. If it's missing, corrupt, or incomplete, it regenerates it with some defaults
		/// </summary>
		/// <remarks>
		/// The old (potentially corrupted) file is saved as a ".old" file alongside the current one, in case this function misdetects a valid file as invalid.
		/// </remarks>
		protected void CheckConfig()
		{
			if (File.Exists(ConfigFilePath))
			{
				if (ValidateConfigJson())
				{
					return;
				}

				if (File.Exists($"{ConfigFilePath}.old"))
				{
					File.Delete($"{ConfigFilePath}.old");
				}
				File.Move(ConfigFilePath, $"{ConfigFilePath}.old");
			}
			Console.WriteLine($"[{Path.GetFileNameWithoutExtension(ConfigFilePath)}] Configuration file seems to be invalid or non-existent, regenerating at {ConfigFilePath}...");

			CreateConfig();
		}

		/// <summary>
		/// Ensures that the live and in-disk configurations are synced.
		/// </summary>
		public void UpdateConfig()
		{
			JsonElm = GetConfig();
			UpdateLiveProps();
		}

		private bool ValidateConfigJson()
		{
			try
			{
				var jsonFile = JsonDocument.Parse(File.ReadAllText(ConfigFilePath));

				var requiredProperties = RequiredProperties.ToList();

				foreach (var jsonProperty in jsonFile.RootElement.EnumerateObject())
				{
					if (requiredProperties.Contains(jsonProperty.Name) && jsonProperty.Value.ValueKind != JsonValueKind.Null)
					{
						requiredProperties.Remove(jsonProperty.Name);
					}
				}

				return requiredProperties.Count == 0;
			}
			catch (JsonException)
			{
				return false;
			}
		}

		private protected abstract void UpdateLiveProps();

		private protected abstract void CreateConfig();
	}

	/// <summary>
	/// Class of configuration relating to most general API configurations, such as the name of the instance, cache delay, and paths for other configuration files.
	/// </summary>
	public sealed class MainConfig : ConfigTemplate
	{
		public MainConfig() : base(Path.Combine(BaseConfigPath, "ApiConfiguration.json"))
		{
			try
			{
				BackendName = JsonElm.GetProperty(nameof(BackendName)).GetString()!;
			}
			catch (KeyNotFoundException)
			{
				BackendName = "Bayt API Host";
			}
			WatchedMountsConfigPath = JsonElm.GetProperty(nameof(WatchedMountsConfigPath)).GetString()!;
			SecondsToUpdate = JsonElm.GetProperty(nameof(SecondsToUpdate)).GetUInt16();
			NetworkPort = JsonElm.TryGetProperty(nameof(NetworkPort), out _) ? JsonElm.GetProperty(nameof(NetworkPort)).GetUInt16() : (ushort) 5899;
		}

		/// <summary>
		/// Contains the user-defined name for this instance of Bayt.
		/// </summary>
		public string BackendName { get; set; }
		/// <summary>
		/// Relative path for the watched mounts configuration file
		/// </summary>
		public string WatchedMountsConfigPath { get; set; }
		/// <summary>
		/// Seconds until data is considered stale and needs a refresh.
		/// </summary>
		/// <remarks>
		///	Set this to 0 to essentially turn the cache off
		/// </remarks>
		public ushort SecondsToUpdate { get; set; }
		public ushort NetworkPort { get; set; }

		protected override List<string> RequiredProperties => ConfigPropertyTypes.MainConfigProperties.RequiredProperties;

		private protected override void UpdateLiveProps()
		{
			BackendName = JsonElm.GetProperty(nameof(BackendName)).GetString() ?? BackendName;
			WatchedMountsConfigPath = JsonElm.GetProperty(nameof(WatchedMountsConfigPath)).GetString() ?? WatchedMountsConfigPath;
			SecondsToUpdate = JsonElm.GetProperty(nameof(SecondsToUpdate)).GetUInt16();
			NetworkPort = JsonElm.GetProperty(nameof(NetworkPort)).GetUInt16();
		}

		private protected override void CreateConfig()
		{
			File.WriteAllText(ConfigFilePath,
				JsonSerializer.Serialize(new Dictionary<string, dynamic>
				{
					{"ConfigVersion", 1},
					{"BackendName", "Bayt API Host"},
					{"WatchedMountsConfigPath", "WatchedMounts.json"},
					{"SecondsToUpdate", 5},
					{"NetworkPort", 5899}
				}));
		}

		/// <summary>
		/// Provides edit access to the configuration, both live and in-disk.
		/// </summary>
		/// <param name="newProps">
		///	Has to have more than one element to edit.
		/// </param>
		/// <param name="addNew">
		///	Whether to allow the addition of new properties.
		/// </param>
		public void EditConfig(Dictionary<string, dynamic> newProps, bool addNew = false)
		{
			if (newProps.Count == 0)
			{
				return;
			}

			var newConfig = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ConfigFilePath)) ?? new Dictionary<string, string>();

			foreach (var newPropsKvp in newProps)
			{
				if (!addNew && !newConfig.ContainsKey(newPropsKvp.Key))
				{
					continue;
				}

				newConfig[newPropsKvp.Key] = newPropsKvp.Value;
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}
	}

	/// <summary>
	/// Class of configuration containing and relating only to the list of watched mountpoints for this instance.
	/// </summary>
	public sealed class WatchedMountsConfig : ConfigTemplate
	{
		public WatchedMountsConfig() : base(Path.Combine(BaseConfigPath, MainConfigs.WatchedMountsConfigPath))
		{
			WatchedMounts = GetConfig();
		}

		public Dictionary<string, string> WatchedMounts { get; private set; }
		protected override List<string> RequiredProperties => ConfigPropertyTypes.WatchedMountsConfigProperties.RequiredProperties;

		private Dictionary<string, string> GetConfig()
		{
			CheckConfig();
			var newConfig = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(ConfigFilePath)) ?? new Dictionary<string, Dictionary<string, string>>();
			return newConfig.Values.First();
		}


		private protected override void UpdateLiveProps()
		{
			WatchedMounts = GetConfig();
		}

		private protected override void CreateConfig()
		{
			File.WriteAllText(ConfigFilePath,
				JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>>
				{
					{ "WatchedMounts", new()
						{ {"/", "Root"} }
					}
				}));
		}

		// Add / Remove

		/// <summary>
		/// Add a list of mountpoints to the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoints">The list of mountpoints to add</param>
		public void Add(Dictionary<string, string> mountPoints)
		{
			if (mountPoints.Count == 0)
			{
				return;
			}

			var newConfig = GetConfig();

			foreach (var mountPointToAdd in mountPoints)
			{
				if (newConfig.ContainsKey(mountPointToAdd.Key))
				{
					continue;
				}

				newConfig.Add(mountPointToAdd.Key, mountPointToAdd.Value);
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}

		/// <summary>
		/// Remove a single mountpoint from the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoint">The mountpoint to remove</param>
		/// <seealso cref="Remove(List{string})"/>
		public void Remove(string mountPoint)
		{
			var newConfig = GetConfig();

			if (!newConfig.Remove(mountPoint))
			{
				return;
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}

		/// <summary>
		/// Remove a list of mountpoints from the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoints">The list of mountpoints to remove</param>
		/// <seealso cref="Remove(string)"/>
		public void Remove(List<string> mountPoints)
		{
			var newConfig = GetConfig();

			foreach (var mountPoint in mountPoints)
			{
				newConfig.Remove(mountPoint);
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}
	}


	public static MainConfig MainConfigs { get; } = new();
	public static WatchedMountsConfig WatchedMountsConfigs { get; } = new();
}