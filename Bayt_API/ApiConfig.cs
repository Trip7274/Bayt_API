using System.Text.Json;

namespace Bayt_API;

public static class ApiConfig
{
	public const string Version = "0.4.5";
	public const byte ApiVersion = 0;
	public static readonly string BaseApiUrlPath = $"/api/v{ApiVersion}";
	public static DateTime LastUpdated { get; set; }
	public static readonly string BaseExecutablePath = Path.GetDirectoryName(Environment.ProcessPath) ?? throw new Exception("Could not get process path");
	private static readonly string BaseConfigPath = Path.Combine(BaseExecutablePath, "config");


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
		private protected abstract List<string> RequiredProperties { get; init; }

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

		private protected abstract void UpdateLiveProps();

		private protected abstract void CreateConfig();

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
	}

	/// <summary>
	/// Class of configuration relating to most general API configurations, such as the name of the instance, cache delay, and paths for other configuration files.
	/// </summary>
	public sealed class MainConfig : ConfigTemplate
	{
		public MainConfig() : base(Path.Combine(BaseConfigPath, "ApiConfiguration.json"))
		{
			BackendName = JsonElm.GetProperty(nameof(BackendName)).GetString() ?? "Bayt API Host";
			WatchedMountsConfigPath = JsonElm.GetProperty(nameof(WatchedMountsConfigPath)).GetString() ?? "WatchedMounts.json";
			SecondsToUpdate = JsonElm.GetProperty(nameof(SecondsToUpdate)).GetUInt16();

			RequiredProperties = ["ConfigVersion", "SecondsToUpdate", "BackendName", "WatchedMountsConfigPath"];
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

		private protected override List<string> RequiredProperties { get; init; }

		private protected override void UpdateLiveProps()
		{
			BackendName = JsonElm.GetProperty(nameof(BackendName)).GetString() ?? BackendName;
			WatchedMountsConfigPath = JsonElm.GetProperty(nameof(WatchedMountsConfigPath)).GetString() ?? WatchedMountsConfigPath;
			SecondsToUpdate = JsonElm.GetProperty(nameof(SecondsToUpdate)).GetUInt16();
		}

		private protected override void CreateConfig()
		{
			File.WriteAllText(ConfigFilePath,
				JsonSerializer.Serialize(new
				{
					ConfigVersion = 1,
					BackendName = "Bayt API Host",
					WatchedMountsConfigPath = "WatchedMounts.json",
					SecondsToUpdate = 5
				})); // TODO: Switch to more strongly-typed config handling
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

			var newConfig = JsonSerializer.Deserialize<Dictionary<string, dynamic>>(File.ReadAllText(ConfigFilePath)) ?? new Dictionary<string, dynamic>();

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
			WatchedMounts = JsonElm.GetProperty(nameof(WatchedMounts)).EnumerateArray().Select(x => x.GetString()).ToList()!;
			RequiredProperties = ["ConfigVersion", "WatchedMounts"];
		}

		public List<string> WatchedMounts { get; set; }
		private protected override List<string> RequiredProperties { get; init; }

		private Dictionary<string, List<string>> GetConfig()
		{
			var newConfig = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(ConfigFilePath)) ?? new Dictionary<string, List<string>>();

			if (newConfig.ContainsKey(nameof(WatchedMounts))) return newConfig;

			CheckConfig();
			newConfig = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(ConfigFilePath)) ?? new Dictionary<string, List<string>>();
			return newConfig;
		}


		private protected override void UpdateLiveProps()
		{
			WatchedMounts = JsonElm.GetProperty(nameof(WatchedMounts)).EnumerateArray().Select(x => x.GetString()).ToList()!;
		}

		private protected override void CreateConfig()
		{
			File.WriteAllText(ConfigFilePath,
				JsonSerializer.Serialize(new
				{
					ConfigVersion = 1,
					WatchedMounts = (List<string>) ["/"]
				})); // TODO: Switch to more strongly-typed config handling
		}

		// Add / Remove

		/// <summary>
		/// Add a single mountpoint to the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoint">The mountpoint to add</param>
		/// <seealso cref="Add(List{string})"/>
		public void Add(string mountPoint)
		{
			var newConfig = GetConfig();

			if (newConfig.Values.First().Contains(mountPoint))
			{
				return;
			}

			newConfig.Values.First().Add(mountPoint);

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}
		/// <summary>
		/// Add a list of mountpoints to the configuration, both live and in-disk.
		/// </summary>
		/// <param name="mountPoints">The list of mountpoints to add</param>
		/// <seealso cref="Add(string)"/>
		public void Add(List<string> mountPoints)
		{
			var newConfig = GetConfig();

			foreach (var mountPointToAdd in mountPoints.ToList())
			{
				if (newConfig.Values.First().Contains(mountPointToAdd))
				{
					mountPoints.Remove(mountPointToAdd);
				}
			}

			newConfig.Values.First().AddRange(mountPoints);

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

			if (!newConfig.Values.First().Contains(mountPoint))
			{
				return;
			}

			newConfig.Values.First().Remove(mountPoint);
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
				newConfig.Values.First().Remove(mountPoint);
			}

			File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(newConfig));
			UpdateConfig();
		}
	}


	public static MainConfig MainConfigs { get; } = new();
	public static WatchedMountsConfig WatchedMountsConfigs { get; } = new();
}