using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using MessagePack;

namespace Sofa_API.Security;

public abstract class HasPermissions
{
	public abstract Dictionary<string, List<string>> PermissionList { get; protected set; }

	public bool HasPermission(Permissions.SofaPermission permissionRequirement)
	{
		if (PermissionList.ContainsKey("admin")) return true;

		if (PermissionList.Count == 0 || !PermissionList.TryGetValue(permissionRequirement.PermissionString, out var userPermissionString)) return false;

		if (!Permissions.SofaPermission.TryParse(permissionRequirement.PermissionString + ':' + string.Join(',', userPermissionString), out var userPermission)) return false;

		return permissionRequirement.Allows(userPermission.PermPowers);
	}

	public void AddPermission(Permissions.SofaPermission permission)
	{
		if (PermissionList.TryGetValue(permission.PermissionString, out var list))
		{
			list.AddRange(permission.PermPowers.Where(p => !list.Contains(p)));
			PermissionList[permission.PermissionString] = list;
		}
		else
		{
			PermissionList.Add(permission.PermissionString, permission.PermPowers);
		}
	}
	public void RemovePermission(Permissions.SofaPermission permission)
	{
		if (!PermissionList.TryGetValue(permission.PermissionString, out var list)) return;

		list.RemoveAll(p => permission.PermPowers.Contains(p));
		if (list.Count == 0) PermissionList.Remove(permission.PermissionString);
		else PermissionList[permission.PermissionString] = list;
	}
	public void SetPermissions(Dictionary<string, List<string>> permissions)
	{
		PermissionList = permissions;
	}

	public static HasPermissions? FetchRequester(Guid identifier)
	{
		if (Clients.TryFetchValidClient(identifier, out var client))
		{
			return client;
		}

		return Users.FetchUser(identifier);
	}
	public static bool TryFetchRequester([NotNullWhen(true)] Guid? identifier, [NotNullWhen(true)] out HasPermissions? requester)
	{
		if (identifier is null)
		{
			requester = null;
			return false;
		}
		if (Clients.TryFetchValidClient(identifier, out var client))
		{
			requester = client;
			return true;
		}
		if (Users.TryFetchUser(identifier, out var user))
		{
			requester = user;
			return true;
		}
		requester = null;
		return false;
	}
}

[MessagePackObject(true, AllowPrivate = true)]
public sealed partial class User : HasPermissions, IEquatable<User>
{
	[SerializationConstructor]
	private User (string username, string? profilePictureUrl, Guid guid, byte[] password, byte[] salt, Dictionary<string, List<string>> permissionList, bool isPaused)
	{
		if (password.Length != Hashing.HashLength) throw new ArgumentException("Password is invalid.", nameof(password));
		if (salt.Length != Hashing.SaltLength) throw new ArgumentException("Salt is invalid", nameof(salt));

		// Checks done

		Username = username;
		ProfilePictureUrl = profilePictureUrl;
		Guid = guid;
		Password = password;
		Salt = salt;
		PermissionList = permissionList;
		IsPaused = isPaused;
	}
	public User(string username, string password, string? profilePictureUrl, Dictionary<string, List<string>>? permissions = null, bool isPaused = false)
	{
		if (Users.DoesUserExist(username)) throw new ArgumentException("Username already exists", nameof(username));
		if (username.Length > 32) throw new ArgumentException("Username too long", nameof(username));
		if (password.Length > 1024) throw new ArgumentException("Password too long", nameof(password));

		profilePictureUrl = ParsingMethods.SanitizeString(profilePictureUrl);
		if (profilePictureUrl is not null &&
		    (string.IsNullOrWhiteSpace(profilePictureUrl) ||
		    !profilePictureUrl.StartsWith("https://"))) throw new ArgumentException("Invalid profile picture URL", nameof(profilePictureUrl));

		password = ParsingMethods.SanitizeString(password);

		Username = ParsingMethods.SanitizeString(username);
		ProfilePictureUrl = profilePictureUrl;
		Guid = Guid.NewGuid();

		var hashedPassword = Hashing.HashPassword(password, Guid, out var salt, out var attributes);
		Password = hashedPassword;
		Salt = salt;
		Attributes = attributes;

		if (permissions is not null)
		{
			PermissionList = permissions;
		}
		IsPaused = isPaused;
	}

	public string Username { get; private set; }

	public Guid Guid { get; }
	public bool IsPaused { get; private set; }
	/// <summary>
	/// A publicly accessible URL to the user's profile picture.
	/// </summary>
	public string? ProfilePictureUrl { get; set; }


	/// <summary>
	/// The user's hashed password.
	/// </summary>
	public byte[] Password { get; private set; }
	public byte[] Salt { get; private set; }
	public Hashing.PasswordAttributes Attributes { get; private set; }

	public override Dictionary<string, List<string>> PermissionList { get; protected set; } = [];


	public bool Edit(Dictionary<string, string?> changes)
	{
		var changesMade = false;
		foreach (var (key, value) in changes)
		{
			switch (key)
			{
				case nameof(Username):
				{
					if (string.IsNullOrWhiteSpace(value) || Username == value)
						continue;

					Username = ParsingMethods.SanitizeString(value);
					break;
				}
				case nameof(ProfilePictureUrl):
				{
					if (value is null)
					{
						ProfilePictureUrl = null;
						break;
					}

					if (ProfilePictureUrl == value || !value.StartsWith("https://"))
						continue;

					ProfilePictureUrl = ParsingMethods.SanitizeString(value);
					break;
				}
				case nameof(Password):
				{
					if (string.IsNullOrWhiteSpace(value))
						continue;

					var hashedPassword = Hashing.HashPassword(value, Guid, out var salt, out var attributes);
					Password = hashedPassword;
					Salt = salt;
					Attributes = attributes;
					break;
				}
				case nameof(IsPaused):
				{
					if (value is null || !bool.TryParse(value, out var result) || IsPaused == result) continue;
					IsPaused = result;
					break;
				}

				default:
				{
					throw new ArgumentException("Invalid key", key);
				}
			}

			changesMade = true;
		}
		if (changesMade) SecurityStores.SaveUser(this);
		return changesMade;
	}
	public void Pause() => IsPaused = true;
	public void Unpause() => IsPaused = false;

	public Dictionary<string, dynamic?> ToDictionary()
	{
		return new()
		{
			{ nameof(Username), Username },
			{ nameof(ProfilePictureUrl), ProfilePictureUrl },
			{ nameof(Guid), Guid.ToString() },
			{ nameof(IsPaused), IsPaused },
			{ nameof(PermissionList), PermissionList }
		};
	}


	public static bool operator ==(User? left, User? right)
	{
		if (left is null && right is null) return true;
		if (left is null || right is null) return false;

		return left.Guid == right.Guid;
	}
	public static bool operator !=(User? left, User? right)
	{
		if (left is null && right is null) return false;
		if (left is null || right is null) return true;

		return left.Guid != right.Guid;
	}
	public bool Equals(User? other)
	{
		if (other is null) return false;
		return ReferenceEquals(this, other) || Guid.Equals(other.Guid);
	}
	public override bool Equals(object? obj)
	{
		return ReferenceEquals(this, obj) || obj is User other && Equals(other);
	}
	public override int GetHashCode()
	{
		return Guid.GetHashCode();
	}
}
[MessagePackObject(true, AllowPrivate = true)]
public sealed partial class Client : HasPermissions, IEquatable<Client>
{
	[SerializationConstructor]
	private Client (string clientName, string thumbprint, Guid guid, Dictionary<string, List<string>>? permissionList = null, bool isPaused = false, bool hasAcknowledgedFutureCert = false, ClientPermissionRequest? pendingPermissionRequest = null)
	{
		if (string.IsNullOrWhiteSpace(clientName))
			throw new ArgumentException("Client name cannot be empty", nameof(clientName));

		if (thumbprint.Length != 64) throw new ArgumentException("Invalid thumbprint", nameof(thumbprint));
		if (clientName.Length > 128) throw new ArgumentException("Client name too long", nameof(clientName));

		// Checks done

		ClientName = ParsingMethods.SanitizeString(clientName);

		Thumbprint = thumbprint;
		Guid = guid;
		if (permissionList is not null) PermissionList = permissionList;
		IsPaused = isPaused;
		HasAcknowledgedFutureCert = hasAcknowledgedFutureCert;
		PendingPermissionRequest = pendingPermissionRequest;

		if (pendingPermissionRequest is not null)
		{
			if (pendingPermissionRequest.Expired)
			{
				ClearRequestedPermissions();
			}
			else
			{
				_ = Task.Run(() => Task.Delay(pendingPermissionRequest.ExpirationTime - DateTime.UtcNow).ContinueWith(_ => ClearRequestedPermissions()));
			}
		}
	}
	public Client (string clientName, string thumbprint, Dictionary<string, List<string>>? permissionList = null, bool isPaused = false)
	{
		if (string.IsNullOrWhiteSpace(clientName))
			throw new ArgumentException("Client name cannot be empty", nameof(clientName));

		if (thumbprint.Length != 64) throw new ArgumentException("Invalid thumbprint", nameof(thumbprint));
		if (clientName.Length > 128) throw new ArgumentException("Client name too long", nameof(clientName));

		// Checks done

		ClientName = ParsingMethods.SanitizeString(clientName);

		Thumbprint = thumbprint;
		Guid = Guid.NewGuid();
		if (permissionList is not null && permissionList.Count > 0)
		{
			// Convert all permissionStrings and permissionPowers to all-lowercase
			PermissionList = permissionList.ToDictionary(
				permissionString => permissionString.Key.ToLowerInvariant(),
				permissionPowers => permissionPowers.Value.Select(permPower => permPower.ToLowerInvariant()).ToList()
			);
		}
		IsPaused = isPaused;
	}
	static Client()
	{
		var encoderSettings = new TextEncoderSettings(UnicodeRanges.BasicLatin);
		encoderSettings.AllowCharacters('+');
		CprJsonSerializerOptions = new()
		{
			WriteIndented = true,
			IndentCharacter = '\t',
			IndentSize = 1,
			Encoder = JavaScriptEncoder.Create(encoderSettings)
		};
	}

	public string ClientName { get; private set; }
	public string Thumbprint { get; private set; }
	public Guid Guid { get; }
	public override Dictionary<string, List<string>> PermissionList { get; protected set; } = [];


	public bool IsPaused { get; private set; }
	public bool HasAcknowledgedFutureCert { get; internal set; }


	private static readonly JsonSerializerOptions CprJsonSerializerOptions;
	public ClientPermissionRequest? PendingPermissionRequest { get; private set; }

	public void SetPermissionRequest(ClientPermissionRequest request)
	{
		string directoryPath = Path.Combine(SecurityStores.BaseSecurityPath, "requests", "permissionUpdates");
		string filePath = Path.Combine(directoryPath, $"{ClientNameSlug}.json");

		Directory.CreateDirectory(directoryPath);
		File.Delete(filePath);

		Dictionary<string, dynamic> cprDict = new()
		{
			["RequesterName"] = ClientName,
			["ID"] = Guid,
			["CurrentPermissions"] = PermissionList.ToDictionary(
				entry => entry.Key,
				entry => string.Join(',', entry.Value)),
			["RequestedPermissions"] = request.RequestedPermissionChanges,
			["TimeRequested"] = request.TimeRequested,
			["ExpirationTime"] = request.ExpirationTime,
			["RequestKey"] = Convert.ToBase64String(request.PermissionRequestKey)
		};

		PendingPermissionRequest = request;
		File.WriteAllText(filePath, JsonSerializer.Serialize(cprDict, CprJsonSerializerOptions));
		_ = Task.Run(() => Task.Delay(ApiConfig.ApiConfiguration.ClientRequestLifetime).ContinueWith(_ => ClearRequestedPermissions()));

		SecurityStores.SaveClient(this);
	}
	public void ApplyRequestedPermissions(byte[] requestKey)
	{
		if (PendingPermissionRequest is null)
		{
			throw new InvalidOperationException("Permission request not set");
		}
		if (PendingPermissionRequest.Expired)
		{
			ClearRequestedPermissions();
			throw new InvalidOperationException("Permission request expired");
		}
		if (!PendingPermissionRequest.PermissionRequestKey.SequenceEqual(requestKey))
		{
			throw new InvalidOperationException("Permission request key does not match");
		}

		foreach (var requestedPerm in PendingPermissionRequest.RequestedPermissionChanges)
		{
			switch (requestedPerm.Key[0])
			{
				case '+':
				{
					if (!Permissions.SofaPermission.TryParse(requestedPerm.Key[1..], out var parsedPerm))
					{
						throw new ArgumentException("List contained an invalid permission request", requestedPerm.Key);
					}
					AddPermission(parsedPerm);
					break;
				}
				case '-':
				{
					if (!Permissions.SofaPermission.TryParse(requestedPerm.Key[1..], out var parsedPerm))
					{
						throw new ArgumentException("List contained an invalid permission request", requestedPerm.Key);
					}
					RemovePermission(parsedPerm);
					break;
				}
			}
		}

		ClearRequestedPermissions();
	}
	public void ClearRequestedPermissions()
	{
		var filePath =
			Path.Combine(SecurityStores.BaseSecurityPath, "requests", "permissionUpdates", $"{ClientNameSlug}.json");

		PendingPermissionRequest = null;
		if (File.Exists(filePath))
		{
			File.Delete(filePath);
		}
		SecurityStores.SaveClient(this);
	}


	[IgnoreMember]
	public string ClientNameSlug => ParsingMethods.ConvertTextToSlug(ClientName);
	[IgnoreMember]
	public bool CanRegister =>
		!IsPaused && HasPermission(new Permissions.SofaPermission("client-management", ["register"]));


	public bool Edit(Dictionary<string, string?> changes)
	{
		var changesMade = false;
		foreach (var (key, value) in changes)
		{
			switch (key)
			{
				case nameof(ClientName):
				{
					if (string.IsNullOrWhiteSpace(value) || ClientName == value) continue;
					ClientName = ParsingMethods.SanitizeString(value);
					break;
				}

				default:
				{
					throw new ArgumentException("Invalid key", key);
				}
			}

			changesMade = true;
		}
		if (changesMade) SecurityStores.SaveClient(this);
		return changesMade;
	}

	public bool RefreshCertificate(X509Certificate2 newCertificate, [NotNullWhen(false)] out string? errorMessage)
	{
		errorMessage = null;
		// Run basic certificate checks
		if (newCertificate.IsExpiredOrTooNew())
		{
			errorMessage = "Certificate is not valid yet or has expired";
			return false;
		}

		// Make sure the cert isn't too long-lasting. (4-month max lifespan)
		if (newCertificate.NotAfter - newCertificate.NotBefore > TimeSpan.FromDays(30 * 4))
		{
			errorMessage = "Certificate is too long-lasting. The maximum allowed lifespan is 4 months.";
			return false;
		}

		Thumbprint = newCertificate.GetCertHashString(HashAlgorithmName.SHA256);
		SecurityStores.SaveClient(this);
		Clients.RefreshList();
		return true;
	}


	internal void Unpause()
	{
		IsPaused = false;
		SecurityStores.SaveClient(this);
	}
	public bool Delete()
	{
		IsPaused = true;
		return Clients.RemoveClient(this);
	}
	public Dictionary<string, dynamic?> ToDictionary()
	{
		return new()
		{
			{ nameof(ClientName), ClientName },
			{ nameof(Guid), Guid },
			{ nameof(Thumbprint), Thumbprint },
			{ nameof(IsPaused), IsPaused },
			{ nameof(PermissionList), PermissionList }
		};
	}


	public static bool operator ==(Client? left, Client? right)
	{
		if (left is null && right is null) return true;
		if (left is null || right is null) return false;

		return left.Guid == right.Guid;
	}
	public static bool operator !=(Client? left, Client? right)
	{
		if (left is null && right is null) return false;
		if (left is null || right is null) return true;

		return left.Guid != right.Guid;
	}
	public bool Equals(Client? other)
	{
		if (other is null) return false;
		return ReferenceEquals(this, other) || Guid.Equals(other.Guid);
	}
	public override bool Equals(object? obj)
	{
		return ReferenceEquals(this, obj) || obj is Client other && Equals(other);
	}
	public override int GetHashCode()
	{
		return Guid.GetHashCode();
	}
}

[MessagePackObject(true, AllowPrivate = true)]
public sealed partial record ClientPermissionRequest
{
	[SerializationConstructor]
	private ClientPermissionRequest(byte[] permissionRequestKey, Dictionary<string, string?> requestedPermissionChanges, DateTime timeRequested)
	{
		RequestedPermissionChanges = requestedPermissionChanges;
		PermissionRequestKey = permissionRequestKey;
		TimeRequested = timeRequested;
	}
	public ClientPermissionRequest(Dictionary<string, string?> requestedPermissionChanges)
	{
		if (requestedPermissionChanges.Any(perm => perm.Key[0] is not ('+' or '-') && perm.Key.Length < 4))
		{
			throw new ArgumentException("List contained an incorrectly formatted permission.", nameof(RequestedPermissionChanges));
		}

		RequestedPermissionChanges = requestedPermissionChanges;
		PermissionRequestKey = RandomNumberGenerator.GetBytes(32);
		TimeRequested = DateTime.UtcNow;
	}

	public byte[] PermissionRequestKey { get; }
	public Dictionary<string, string?> RequestedPermissionChanges { get; }
	public DateTime TimeRequested { get; }
	public DateTime ExpirationTime => TimeRequested + ApiConfig.ApiConfiguration.ClientRequestLifetime;
	public bool Expired => DateTime.UtcNow > ExpirationTime;

	public Dictionary<string, dynamic?> ToDictionary(bool includeKey = false)
	{
		return new()
			{
				{ nameof(PermissionRequestKey), includeKey ? Convert.ToBase64String(PermissionRequestKey) : null },
				{ nameof(RequestedPermissionChanges), RequestedPermissionChanges },
				{ nameof(TimeRequested), TimeRequested },
				{ nameof(ExpirationTime), ExpirationTime }
			};
	}
}


public static class Clients
{
	static Clients()
	{
		foreach (var client in SecurityStores.GetAllClients())
		{
			AvailableClients.Add(client.Guid, client);
			AvailableClientThumbs.Add(client.Thumbprint);
		}

		string requestDirPath = Path.Combine(SecurityStores.BaseSecurityPath, "requests", "registrationRequests");
		if (!Directory.Exists(requestDirPath))
		{
			Directory.CreateDirectory(requestDirPath);
			return;
		}

		var lastAcceptableTime = DateTime.UtcNow - ApiConfig.ApiConfiguration.ClientRequestLifetime;
		foreach (var checkedFilePath in Directory.EnumerateFiles(requestDirPath, "*", SearchOption.AllDirectories))
		{
			if (checkedFilePath.EndsWith(".json.failed")) continue;
			if (!checkedFilePath.EndsWith(".json") || File.GetLastWriteTimeUtc(checkedFilePath) < lastAcceptableTime)
			{
				File.Delete(checkedFilePath);
				continue;
			}

			Dictionary<string, JsonElement>? parsedJson;
			try
			{
				parsedJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(checkedFilePath));
			}
			catch (Exception e)
			{
				Logs.LogBook.Write(new (StreamId.Error, "Pending Client Restore", $"Error while restoring client registration request {checkedFilePath}: {e.Message}"));
				File.Move(checkedFilePath, checkedFilePath + ".failed");
				continue;
			}
			if (parsedJson is null)
			{
				Logs.LogBook.Write(new (StreamId.Error, "Pending Client Restore", $"Error while restoring client registration request {checkedFilePath}: Could not parse JSON"));
				File.Move(checkedFilePath, checkedFilePath + ".failed");
				continue;
			}

			if (parsedJson["TimeRequested"].GetDateTime() < lastAcceptableTime || !TryFetchValidClient(parsedJson["ID"].GetGuid(), out var client, true))
			{
				File.Delete(checkedFilePath);
				continue;
			}

			PendingClients.Add(parsedJson["RegistrationKey"].GetString()!, client);
		}
	}
	private static readonly Dictionary<Guid, Client> AvailableClients = [];
	private static readonly HashSet<string> AvailableClientThumbs = [];


	public static Dictionary<string, Client> PendingClients { get; } = [];
	public static void AddPendingClient(string registrationKey, Client client, TimeSpan? ttl = null)
	{
		ttl ??= ApiConfig.ApiConfiguration.ClientRequestLifetime;

		PendingClients.Add(registrationKey, client);
		_ = Task.Run(() => Task.Delay(ttl.Value).ContinueWith(_ => RemovePendingClient(registrationKey, client)));
	}

	public static void RemovePendingClient(string registrationKey, Client client)
	{
		PendingClients.Remove(registrationKey);
		if (client.IsPaused) RemoveClient(client);

		string requestFilePath = Path.Combine(SecurityStores.BaseSecurityPath, "requests", "registrationRequests", $"{client.ClientNameSlug}.json");
		if (File.Exists(requestFilePath))
		{
			File.Delete(requestFilePath);
		}
	}


	public static List<Dictionary<string, dynamic?>> FetchAllClients() =>
		AvailableClients.Select(client => client.Value.ToDictionary()).ToList();
	public static int Count => AvailableClients.Count;

	public static IEnumerable<Client> GetMasterClients => AvailableClients.Values.Where(client => client.CanRegister);

	public static bool DoesClientExist(string thumbprint) => AvailableClientThumbs.Contains(thumbprint);
	public static bool DoesClientExist(Guid guid) => AvailableClients.ContainsKey(guid);

	public static Client? FetchValidClient(string thumbprint)
	{
		if (!DoesClientExist(thumbprint)) return null;

		return AvailableClients.FirstOrDefault(client => client.Value.Thumbprint == thumbprint).Value;
	}

	/// <summary>
	/// Attempts to retrieve a valid client object with the provided client's certificate thumbprint.
	/// </summary>
	/// <param name="thumbprint">The SHA-256 thumbprint of the client's certificate.</param>
	/// <param name="client">The client object, if found.</param>
	/// <param name="allowPaused">Whether to count paused clients as valid. Defaults to false.</param>
	/// <returns>True if a valid client object is found.</returns>
	/// <remarks>
	/// Do note that the <c cref="client">client</c> parameter might be set, while the method still returned false. This means that, while a client was found, it was set to be paused. <br/><br/>
	/// This method is less efficient than <see cref="TryFetchValidClient(Guid?, out Client?, bool)"/>, as it has to iterate through all clients, thus it should only be used when the other method is inapplicable.
	/// </remarks>
	public static bool TryFetchValidClient([NotNullWhen(true)] string? thumbprint, [NotNullWhen(true)] out Client? client, bool allowPaused = false)
	{
		if (string.IsNullOrWhiteSpace(thumbprint) || !DoesClientExist(thumbprint))
		{
			client = null;
			return false;
		}

		client = AvailableClients.FirstOrDefault(client => client.Value.Thumbprint == thumbprint).Value;
		return client is not null && (!client.IsPaused || allowPaused);
	}

	///  <summary>
	///  Attempts to retrieve a valid client object with the provided client's GUID.
	///  </summary>
	///  <param name="guid">The GUID of the client to retrieve.</param>
	///  <param name="client">The client object, if found.</param>
	///  <param name="allowPaused">Whether to count paused clients as valid. Defaults to false.</param>
	///  <returns>True if a valid client object is found and is not paused (or paused, if <c>allowPaused</c> is set).</returns>
	///  <remarks>
	///	 Do note that the <c cref="client">client</c> parameter might be set, while the method still returned false. This means that, while a client was found, it was set to be paused. <br/><br/>
	///  This method is more efficient than <see cref="TryFetchValidClient(string?, out Client?, bool)"/>, as it does not have to iterate through all clients, thus it should be used whenever possible.
	///  </remarks>
	public static bool TryFetchValidClient([NotNullWhen(true)] Guid? guid, [NotNullWhen(true)] out Client? client, bool allowPaused = false)
	{
		if (guid is null || !DoesClientExist(guid.Value))
		{
			client = null;
			return false;
		}

		client = AvailableClients.GetValueOrDefault(guid.Value);
		return client is not null && (!client.IsPaused || allowPaused);
	}

	public static void RefreshList()
	{
		AvailableClients.Clear();
		AvailableClientThumbs.Clear();

		foreach (var client in SecurityStores.GetAllClients())
		{
			AvailableClients.Add(client.Guid, client);
			AvailableClientThumbs.Add(client.Thumbprint);
		}
	}

	public static void AddClient(Client client)
	{
		SecurityStores.SaveClient(client);

		AvailableClientThumbs.Add(client.Thumbprint);
		AvailableClients.Add(client.Guid, client);
	}
	public static bool RemoveClient(Client client)
	{
		if (!SecurityStores.RemoveClient(client)) return false;

		AvailableClientThumbs.Remove(client.Thumbprint);
		AvailableClients.Remove(client.Guid);
		return true;
	}
}
public static class Users
{
	static Users()
	{
		foreach (var user in SecurityStores.GetAllUsers())
		{
			AvailableUsers.Add(user.Guid, user);
			AvailableUsernames.Add(user.Username);
		}
	}
	private static readonly Dictionary<Guid, User> AvailableUsers = [];
	private static readonly HashSet<string> AvailableUsernames = [];

	public static List<Dictionary<string, dynamic?>> FetchAllUsers() =>
		AvailableUsers.Select(user => user.Value.ToDictionary()).ToList();
	public static int Count => AvailableUsers.Count;

	public static bool DoesUserExist(Guid userId) => AvailableUsers.ContainsKey(userId);
	public static bool DoesUserExist(string username) => AvailableUsernames.Contains(ParsingMethods.SanitizeString(username));

	public static User? FetchUser(string username)
	{
		return TryFetchUser(username, out var user) ? user : null;
	}
	public static User? FetchUser(Guid guid)
	{
		return TryFetchUser(guid, out var user) ? user : null;
	}
	public static bool TryFetchUser([NotNullWhen(true)] string? username, [NotNullWhen(true)] out User? user)
	{
		if (string.IsNullOrWhiteSpace(username) || !DoesUserExist(username))
		{
			user = null;
			return false;
		}
		username = ParsingMethods.SanitizeString(username);

		user = AvailableUsers.FirstOrDefault(user => user.Value.Username == username).Value;
		return user is not null && !user.IsPaused;
	}
	public static bool TryFetchUser([NotNullWhen(true)] Guid? guid, [NotNullWhen(true)] out User? user)
	{
		if (guid is null || !DoesUserExist(guid.Value))
		{
			user = null;
			return false;
		}

		user = AvailableUsers.GetValueOrDefault(guid.Value);
		return user is not null && !user.IsPaused;
	}

	public static bool AuthenticateUser(string username, string presentedPassword, [NotNullWhen(true)] out User? user)
	{
		username = ParsingMethods.SanitizeString(username);
		presentedPassword = ParsingMethods.SanitizeString(presentedPassword);

		if (TryFetchUser(username, out var foundUser) && !foundUser.IsPaused &&
		    foundUser.Password.SequenceEqual(Hashing.HashPassword(presentedPassword, foundUser.Guid, foundUser.Salt, foundUser.Attributes)))
		{
			user = foundUser;
			return true;
		}
		user = null;
		return false;
	}

	public static void RefreshList()
	{
		AvailableUsers.Clear();
		AvailableUsernames.Clear();
		foreach (var user in SecurityStores.GetAllUsers())
		{
			AvailableUsers.Add(user.Guid, user);
			AvailableUsernames.Add(user.Username);
		}
	}

	public static void AddUser(User user)
	{
		SecurityStores.SaveUser(user);

		AvailableUsers.Add(user.Guid, user);
		AvailableUsernames.Add(user.Username);
	}
	public static void RemoveUser(User user)
	{
		SecurityStores.RemoveUser(user);

		AvailableUsers.Remove(user.Guid);
		AvailableUsernames.Remove(user.Username);
	}
}