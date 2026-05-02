using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MessagePack;

namespace Sofa_API.Security;


internal static class SecurityStores
{
	static SecurityStores()
	{
		if (!Directory.Exists(BaseSecurityPath))
		{
			Directory.CreateDirectory(BaseSecurityPath);
			File.WriteAllText(Path.Combine(BaseSecurityPath, "README"), "This folder contains Sofa's security data.\n" +
			                                                            "Please do not edit this folder manually, as you may corrupt user and client entries.");
		}
	}

	internal static readonly string BaseSecurityPath = Path.Combine(ApiConfig.BaseDataPath, "security");

	internal static class UserStores
	{
		static UserStores()
		{
			if (!Directory.Exists(BaseUsersPath))
				Directory.CreateDirectory(BaseUsersPath);

			StoreFolderIndex = new("User", BaseUsersPath);
		}

		private static readonly string BaseUsersPath = Path.Combine(BaseSecurityPath, "registered", "users");
		private static readonly StoreIndex StoreFolderIndex;

		internal static IEnumerable<User> GetAllUsers()
		{
			foreach (var indexKvp in StoreFolderIndex)
			{
				var user = GetStoreContents<User>(Path.Combine(BaseUsersPath, indexKvp.Value, "securityData.msgpack"));
				if (user is null)
				{
					Logs.LogBook.Write(new (StreamId.Verbose, "User Security Store", $"The user's security data file ({indexKvp.Value}) is missing. Updating index..."));
					StoreFolderIndex.DeleteIndexEntry(indexKvp.Key);
				}
				else
				{
					yield return user;
				}
			}
		}

		internal static User? FetchUser(Guid userId)
		{
			if (StoreFolderIndex.TryGetEntry(userId, out var userNameSlug))
			{
				var user = GetStoreContents<User>(Path.Combine(BaseUsersPath, userNameSlug, "securityData.msgpack"));
				if (user is null)
				{
					Logs.LogBook.Write(new (StreamId.Verbose, "User Security Store", $"The user's security data file ({userNameSlug}) is missing. Updating index..."));
					StoreFolderIndex.DeleteIndexEntry(userId);
				}
				return user;
			}
			else
			{
				return null;
			}
		}
		internal static User? FetchUser(string username) => GetAllUsers().FirstOrDefault(u => u.Name == username);

		internal static bool TryFetchUser([NotNullWhen(true)] string? username, [NotNullWhen(true)] out User? user)
		{
			if (string.IsNullOrWhiteSpace(username))
			{
				user = null;
				return false;
			}
			user = GetAllUsers().FirstOrDefault(u => u.Name == username);
			return user is not null;
		}
		internal static bool TryFetchUser([NotNullWhen(true)] Guid? userId, [NotNullWhen(true)] out User? user)
		{
			if (userId is null || !StoreFolderIndex.TryGetEntry(userId.Value, out var userNameSlug))
			{
				user = null;
				return false;
			}
			user = GetStoreContents<User>(Path.Combine(BaseUsersPath, userNameSlug, "securityData.msgpack"));
			return user is not null;
		}

		internal static void SaveUser(User user)
		{
			string securityDirectoryPath = Path.Combine(BaseUsersPath, user.NameSlug);
			string securityFilePath = Path.Combine(securityDirectoryPath, "securityData.msgpack");

			// If the user's name was changed from the index, update the index and folder structure.
			if (StoreFolderIndex.TryGetEntry(user.Guid, out var folderName) && folderName != user.NameSlug)
			{
				string oldFolderPath = Path.Combine(BaseUsersPath, folderName);

				// If a different name is in the index but the corresponding folder does not exist, something must've gone wrong.
				if (!Directory.Exists(oldFolderPath))
				{
					Logs.LogBook.Write(new (StreamId.Error, "User Store Saving",
						$"The user's current name ({user.NameSlug}) does not match the index ({folderName}) and does not seem to have been changed recently. " +
						$"The index will be rebuilt..."));
					StoreFolderIndex.BuildIndex();
				}
				else
				{
					StoreFolderIndex.UpdateIndex(user.Guid, user.NameSlug);
					Directory.Move(oldFolderPath, securityDirectoryPath);
				}
			}

			// If the user does not exist in the index, add it.
			if (folderName is null)
			{
				StoreFolderIndex.UpdateIndex(user.Guid, user.NameSlug);
			}

			Directory.CreateDirectory(securityDirectoryPath);
			File.WriteAllBytes(securityFilePath, MessagePackSerializer.Serialize(user));
		}
		internal static void RemoveUser(User user, bool keepOtherData)
		{
			string securityDirectoryPath = Path.Combine(BaseUsersPath, user.NameSlug);
			string securityFilePath = Path.Combine(securityDirectoryPath, "securityData.msgpack");

			if (File.Exists(securityFilePath))
				File.Delete(securityFilePath);

			if (!keepOtherData)
				Directory.Delete(securityDirectoryPath);

			StoreFolderIndex.DeleteIndexEntry(user.Guid);
		}
	}
	internal static class ClientStores
	{
		static ClientStores()
		{
			if (!Directory.Exists(BaseClientsPath))
				Directory.CreateDirectory(BaseClientsPath);

			StoreFolderIndex = new("Client", BaseClientsPath);
		}

		private static readonly string BaseClientsPath = Path.Combine(BaseSecurityPath, "registered", "clients");
		private static readonly StoreIndex StoreFolderIndex;

		internal static IEnumerable<Client> GetAllClients()
		{
			foreach (var indexKvp in StoreFolderIndex)
			{
				var client = GetStoreContents<Client>(Path.Combine(BaseClientsPath, indexKvp.Value, "securityData.msgpack"));
				if (client is not null)
				{
					yield return client;
				}
				else
				{
					Logs.LogBook.Write(new (StreamId.Verbose, "Client Security Store", $"The client's security data file ({indexKvp.Value}) is missing. Updating index..."));
					StoreFolderIndex.DeleteIndexEntry(indexKvp.Key);
				}
			}
		}

		internal static Client? FetchClient(string thumbprint) => GetAllClients().FirstOrDefault(client => client.Thumbprint == thumbprint);
		internal static Client? FetchClient(Guid guid)
		{
			if (!StoreFolderIndex.TryGetValue(guid, out var targetClientFolder))
			{
				return null;
			}

			var client = GetStoreContents<Client>(Path.Combine(BaseClientsPath, targetClientFolder, "securityData.msgpack"));
			if (client is null)
			{
				Logs.LogBook.Write(new (StreamId.Verbose, "Client Security Store", $"The client's security data file ({targetClientFolder}) is missing. Updating index..."));
				StoreFolderIndex.DeleteIndexEntry(guid);
			}
			return client;
		}

		internal static bool TryFetchClient([NotNullWhen(true)] string? thumbprint, [NotNullWhen(true)] out Client? client)
		{
			if (string.IsNullOrWhiteSpace(thumbprint))
			{
				client = null;
				return false;
			}

			client = FetchClient(thumbprint);
			return client is not null;
		}
		internal static bool TryFetchClient([NotNullWhen(true)] Guid? guid, [NotNullWhen(true)] out Client? client)
		{
			if (guid is null)
			{
				client = null;
				return false;
			}

			client = FetchClient(guid.Value);
			return client is not null;
		}

		internal static void SaveClient(Client client)
		{
			string securityDirectoryPath = Path.Combine(BaseClientsPath, client.NameSlug);
			string securityFilePath = Path.Combine(securityDirectoryPath, "securityData.msgpack");

			// If the client's name was changed from the index, update the index and folder structure.
			if (StoreFolderIndex.TryGetEntry(client.Guid, out var folderName) && folderName != client.NameSlug)
			{
				string oldFolderPath = Path.Combine(BaseClientsPath, folderName);

				// If a different name is in the index but the corresponding folder does not exist, something must've gone wrong.
				if (!Directory.Exists(oldFolderPath))
				{
					Logs.LogBook.Write(new (StreamId.Error, "Client Auth Saving",
						$"The client's current name ({client.NameSlug}) does not match the index ({folderName}) and does not seem to have been changed recently. " +
						$"The index will be rebuilt..."));
					StoreFolderIndex.BuildIndex();
				}
				else
				{
					StoreFolderIndex.UpdateIndex(client.Guid, client.NameSlug);
					Directory.Move(oldFolderPath, securityDirectoryPath);
				}
			}
			// If the client does not exist in the index, add it.
			if (folderName is null)
			{
				StoreFolderIndex.UpdateIndex(client.Guid, client.NameSlug);
			}

			Directory.CreateDirectory(securityDirectoryPath);
			File.WriteAllBytes(securityFilePath, MessagePackSerializer.Serialize(client));
		}
		internal static void RemoveClient(Client client, bool keepOtherData)
		{
			string securityDirectoryPath = Path.Combine(BaseClientsPath, client.NameSlug);
			string securityFilePath = Path.Combine(securityDirectoryPath, "securityData.msgpack");

			if (File.Exists(securityFilePath))
				File.Delete(securityFilePath);

			if (!keepOtherData || !Directory.EnumerateFileSystemEntries(securityDirectoryPath).Any())
				Directory.Delete(securityDirectoryPath);

			StoreFolderIndex.DeleteIndexEntry(client.Guid);
		}
	}


	private static T? GetStoreContents<T>(string filePath) where T : HasPermissions
	{
		if (!File.Exists(filePath))
			return null;

		var fileData = File.ReadAllBytes(filePath);
		return MessagePackSerializer.Deserialize<T>(fileData);
	}

	private sealed record StoreIndex : IEnumerable<KeyValuePair<Guid, string>>
	{
		public StoreIndex(string storeName,string baseIndexingPath)
		{
			_storeName = storeName;
			_baseIndexingPath = baseIndexingPath;

			// Fetch or build the index.
			string indexFilePath = Path.Combine(baseIndexingPath, ".index.json");
			if (!File.Exists(indexFilePath)) BuildIndex();
			else
			{
				try
				{
					_storeFolderIndex = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(indexFilePath)) ?? [];
				}
				catch (Exception e)
				{
					Logs.LogBook.Write(new (StreamId.Error, $"[{_storeName}] Store Index", $"Failed to load {_storeName} store index: {e.Message}\nReindexing..."));
					BuildIndex();
				}
			}
		}

		private readonly string _storeName;
		private readonly string _baseIndexingPath;
		private readonly Dictionary<Guid, string> _storeFolderIndex = [];
		private readonly Lock _indexLock = new();

		public bool TryGetEntry(Guid guid, [NotNullWhen(true)] out string? clientNameSlug)
		{
			lock (_indexLock)
				return _storeFolderIndex.TryGetValue(guid, out clientNameSlug);
		}
		public bool TryGetValue(Guid guid, [NotNullWhen(true)] out string? clientNameSlug)
		{
			lock (_indexLock)
			{
				clientNameSlug = _storeFolderIndex.GetValueOrDefault(guid);
				return clientNameSlug is not null;
			}
		}


		public void UpdateIndex(Guid guid, string clientNameSlug)
		{
			lock (_indexLock)
			{
				_storeFolderIndex[guid] = clientNameSlug;
				File.WriteAllText(Path.Combine(_baseIndexingPath, ".index.json"), JsonSerializer.Serialize(_storeFolderIndex, ApiConfig.SofaJsonSerializerOptions));
			}
		}
		public void DeleteIndexEntry(Guid guid)
		{
			lock (_indexLock)
			{
				_storeFolderIndex.Remove(guid);
				File.WriteAllText(Path.Combine(_baseIndexingPath, ".index.json"), JsonSerializer.Serialize(_storeFolderIndex, ApiConfig.SofaJsonSerializerOptions));
			}
		}
		public void BuildIndex()
		{
			Logs.LogBook.Write(new (StreamId.Verbose, $"{_storeName} Security Store Index", "Store index is being rebuilt..."));
			lock (_indexLock)
			{
				_storeFolderIndex.Clear();
				foreach (var clientDir in Directory.EnumerateDirectories(_baseIndexingPath))
				{
					string securityDataFilePath = Path.Combine(clientDir, "securityData.msgpack");
					if (!File.Exists(securityDataFilePath)) continue;

					var securityData = GetStoreContents<HasPermissions>(securityDataFilePath)!;
					_storeFolderIndex.Add(securityData.Guid, securityData.NameSlug);
				}
				Logs.LogBook.Write(new (StreamId.Verbose, $"{_storeName} Security Store Index", "Store index has been built."));
				File.WriteAllText(Path.Combine(_baseIndexingPath, ".index.json"), JsonSerializer.Serialize(_storeFolderIndex, ApiConfig.SofaJsonSerializerOptions));
			}
		}

		public IEnumerator<KeyValuePair<Guid, string>> GetEnumerator()
		{
			lock (_indexLock)
				return _storeFolderIndex.GetEnumerator();
		}
		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}