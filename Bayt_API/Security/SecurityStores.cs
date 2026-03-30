using System.Diagnostics.CodeAnalysis;
using MessagePack;

namespace Bayt_API.Security;


internal static class SecurityStores
{
	static SecurityStores()
	{
		if (!Directory.Exists(BaseSecurityPath))
		{
			Directory.CreateDirectory(BaseSecurityPath);
			File.WriteAllText(Path.Combine(BaseSecurityPath, "README"), "This folder contains Bayt's security data.\n" +
			                                                            "Please do not edit this folder manually, as you may corrupt user and client entries.");
		}
		CheckDataStores();
	}

	private static readonly string BaseSecurityPath = Path.Combine(ApiConfig.BaseDataPath, "security");
	private static string SecurityStorePath(StoreSection section) => Path.Combine(BaseSecurityPath, $"store{section.ToString()}.msgpack");

	private static readonly Lock UserStoreWriteLock = new();
	private static readonly Lock ClientStoreWriteLock = new();
	private static Lock GetLock(StoreSection section)
	{
		return section switch
		 {
			 StoreSection.User => UserStoreWriteLock,
			 StoreSection.Client => ClientStoreWriteLock,
			 _ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		 };
	}

	private enum StoreSection : byte
	{
		User,
		Client
	}

	// This is a bit of a mess.

	internal static IEnumerable<User> GetAllUsers()
	{
		var usersDict = GetSection<User>();

		return usersDict.Values;
	}

	internal static User? FetchUser(Guid userId) => GetData<User>(userId);
	internal static User? FetchUser(string username) => GetAllUsers().FirstOrDefault(u => u.Username == username);

	internal static bool TryFetchUser([NotNullWhen(true)] string? username, [NotNullWhen(true)] out User? user)
	{
		if (string.IsNullOrWhiteSpace(username)) { user = null; return false; }
		user = GetAllUsers().FirstOrDefault(u => u.Username == username);
		return user is not null;
	}
	internal static bool TryFetchUser([NotNullWhen(true)] Guid? userId, [NotNullWhen(true)] out User? user)
	{
		if (userId is null) { user = null; return false; }
		user = GetData<User>(userId.Value);
		return user is not null;
	}

	internal static void SaveUser(User user) => SetData(user.Guid, user);
	internal static bool RemoveUser(User user) => RemoveData<User>(user.Guid);


	internal static IEnumerable<Client> GetAllClients()
	{
		var clientsDict = GetSection<Client>();

		return clientsDict.Values;
	}

	internal static Client? FetchClient(string thumbprint) => GetAllClients().FirstOrDefault(client => client.Thumbprint == thumbprint);
	internal static Client? FetchClient(Guid guid) => GetData<Client>(guid);

	internal static bool TryFetchClient([NotNullWhen(true)] string? thumbprint, [NotNullWhen(true)] out Client? client)
	{
		if (string.IsNullOrWhiteSpace(thumbprint)) { client = null; return false; }
		client = FetchClient(thumbprint);
		return client is not null;
	}
	internal static bool TryFetchClient([NotNullWhen(true)] Guid? guid, [NotNullWhen(true)] out Client? client)
	{
		if (guid is null) { client = null; return false; }
		client = FetchClient(guid.Value);
		return client is not null;
	}

	internal static void SaveClient(Client client) => SetData(client.Guid, client);
	internal static bool RemoveClient(Client client) => RemoveData<Client>(client.Guid);


	// Private helper methods


	private static StoreSection PickSection<T>() where T : HasPermissions
	{
		return typeof(T) == typeof(Client) ? StoreSection.Client : StoreSection.User;
	}
	private static Dictionary<string, T> GetStoreContents<T>(StoreSection section, string filePath) where T : HasPermissions
	{
		if (!File.Exists(filePath))
			InitializeDataFile(section, filePath, "The file abruptly disappeared.");

		var fileData = File.ReadAllBytes(filePath);
		return MessagePackSerializer.Deserialize<Dictionary<string, T>>(fileData);
	}

	private static T? GetData<T>(Guid key) where T : HasPermissions
	{
		var section = PickSection<T>();

		var fileContents = GetStoreContents<T>(section, SecurityStorePath(section));

		return fileContents.GetValueOrDefault(key.ToString());
	}
	private static Dictionary<string, T> GetSection<T>() where T : HasPermissions
	{
		// Ah yes, one of the most methods of all time.
		var section = PickSection<T>();

		return GetStoreContents<T>(section, SecurityStorePath(section));
	}
	private static void SetData<T>(Guid key, T value) where T : HasPermissions
	{
		var section = PickSection<T>();

		string securityFilePath = SecurityStorePath(section);
		lock (GetLock(section))
		{
			var fileContents = GetStoreContents<T>(section, securityFilePath);

			fileContents[key.ToString()] = value;
			File.WriteAllBytes(securityFilePath, MessagePackSerializer.Serialize(fileContents));
		}
	}
	private static bool RemoveData<T>(Guid key) where T : HasPermissions
	{
		var section = PickSection<T>();

		string securityFilePath = SecurityStorePath(section);
		lock (GetLock(section))
		{
			var fileContents = GetStoreContents<T>(section, securityFilePath);

			if (fileContents.Remove(key.ToString()))
			{
				File.WriteAllBytes(securityFilePath, MessagePackSerializer.Serialize(fileContents));
				return true;
			}
			return false;
		}
	}


	private static void CheckDataStores()
	{
		foreach (var section in Enum.GetValues<StoreSection>())
		{
			string securityFilePath = SecurityStorePath(section);
			if (!File.Exists(securityFilePath))
			{
				InitializeDataFile(section, securityFilePath);
				continue;
			}
			try
			{
				var fileData = File.ReadAllBytes(securityFilePath);
				switch (section)
				{
					case StoreSection.User:
					{
						MessagePackSerializer.Deserialize<Dictionary<string, User>>(fileData);
						break;
					}
					case StoreSection.Client:
					{
						MessagePackSerializer.Deserialize<Dictionary<string, Client>>(fileData);
						break;
					}
					default:
					{
						continue;
					}
				}
			}
			catch (Exception e)
			{
				InitializeDataFile(section, securityFilePath, e.Message);
			}
		}
	}
	private static void InitializeDataFile(StoreSection section, string? securityFilePath = null, string? errorMessage = null)
	{
		securityFilePath ??= SecurityStorePath(section);

		if (errorMessage is not null)
			Logs.LogBook.Write(new (StreamId.Error, $"Secure Store [{section.ToString()}]", $"There was an error initializing the store file: {errorMessage}"));


		lock (GetLock(section))
		{
			// If an old store file exists, move it to a backup file.
			if (File.Exists(securityFilePath))
			{
				File.Move(securityFilePath, securityFilePath + ".old", true);
				Logs.LogBook.Write(new (StreamId.Warning, $"Secure Store [{section.ToString()}]", $"The old store file has been moved to {securityFilePath}.old"));
			}

			Dictionary<string, object> dict = [];

			File.WriteAllBytes(securityFilePath, MessagePackSerializer.Serialize(dict));
			Logs.LogBook.Write(new (StreamId.Verbose, $"Secure Store [{section.ToString()}]", "The store file has been initialized."));
		}
	}
}