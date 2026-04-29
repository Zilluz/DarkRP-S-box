using Sandbox.UI;

public sealed class AdminSystem : GameObjectSystem<AdminSystem>
{
	const string StoragePath = "server/admins.json";
	const string LegacyStorageKey = "admins";

	public sealed class Entry
	{
		public string DisplayName { get; set; }
		public AdminRole Role { get; set; }
	}

	Dictionary<long, Entry> _entries = new();
	bool _loaded;
	string _lastLoadedStorageText;

	public AdminSystem( Scene scene ) : base( scene )
	{
	}

	public AdminRole GetRole( Connection connection )
	{
		if ( connection is null )
			return AdminRole.None;

		if ( connection.IsHost )
			return AdminRole.SuperAdmin;

		return GetRole( connection.SteamId );
	}

	public AdminRole GetRole( SteamId steamId )
	{
		EnsureLoaded();
		RefreshFromStorageFileIfChanged();
		return _entries.TryGetValue( steamId, out var entry ) ? entry.Role : AdminRole.None;
	}

	public bool HasAdminAccess( Connection connection ) => GetRole( connection ) >= AdminRole.Admin;
	public bool HasSuperAdminAccess( Connection connection ) => GetRole( connection ) >= AdminRole.SuperAdmin;

	public IReadOnlyDictionary<SteamId, Entry> GetEntries()
	{
		if ( !Networking.IsHost )
			return new Dictionary<SteamId, Entry>();

		EnsureLoaded();
		return _entries.ToDictionary( x => (SteamId)x.Key, x => x.Value );
	}

	public void SetRole( SteamId steamId, AdminRole role, string displayName = null )
	{
		Assert.True( Networking.IsHost, "Only the host may modify admin roles." );

		if ( steamId.Value <= 0 )
			return;

		var targetConnection = Connection.All.FirstOrDefault( x => x.SteamId == steamId );
		if ( targetConnection?.IsHost == true )
			return;

		EnsureLoaded();

		if ( role == AdminRole.None )
		{
			if ( _entries.Remove( steamId ) )
			{
				Save();
			}
		}
		else
		{
			_entries[steamId] = new Entry
			{
				DisplayName = string.IsNullOrWhiteSpace( displayName ) ? steamId.ToString() : displayName.Trim(),
				Role = role
			};

			Save();
		}

		ApplyRoleToOnlinePlayer( steamId );
	}

	public void RefreshPlayerRole( Player player )
	{
		if ( !Networking.IsHost || !player.IsValid() )
			return;

		player.SetAdminRole( GetRole( player.Network.Owner ) );
	}

	void ApplyRoleToOnlinePlayer( SteamId steamId )
	{
		var connection = Connection.All.FirstOrDefault( x => x.SteamId == steamId );
		if ( connection is null )
			return;

		RefreshPlayerRole( Player.FindForConnection( connection ) );
	}

	void EnsureLoaded()
	{
		if ( _loaded || !Networking.IsHost )
			return;

		var hasPrimaryStorage = FileSystem.Data.FileExists( StoragePath );
		var hasLegacyStorage = LocalData.Has( LegacyStorageKey );

		_entries = hasPrimaryStorage
			? ReadStorageFile()
			: LocalData.Get<Dictionary<long, Entry>>( LegacyStorageKey, new() ) ?? new();

		_loaded = true;

		if ( !hasPrimaryStorage )
		{
			Save();
		}
	}

	void Save()
	{
		EnsureLoaded();
		var dir = System.IO.Path.GetDirectoryName( StoragePath );
		if ( !string.IsNullOrWhiteSpace( dir ) && !FileSystem.Data.DirectoryExists( dir ) )
		{
			FileSystem.Data.CreateDirectory( dir );
		}

		var payload = _entries.ToDictionary(
			x => x.Key.ToString(),
			x => x.Value.Role switch
			{
				AdminRole.Admin => "admin",
				AdminRole.SuperAdmin => "superadmin",
				_ => "none"
			} );

		_lastLoadedStorageText = Json.Serialize( payload );
		FileSystem.Data.WriteAllText( StoragePath, _lastLoadedStorageText );
	}

	Dictionary<long, Entry> ReadStorageFile()
	{
		_lastLoadedStorageText = FileSystem.Data.FileExists( StoragePath )
			? FileSystem.Data.ReadAllText( StoragePath )
			: null;

		try
		{
			var roleMap = Json.Deserialize<Dictionary<string, string>>( _lastLoadedStorageText );
			if ( roleMap is not null )
			{
				return roleMap
					.Select( x => new
					{
						SteamId = ulong.TryParse( x.Key, out var parsedSteamId ) ? parsedSteamId : 0,
						Role = ParseStoredRole( x.Value )
					} )
					.Where( x => x.SteamId > 0 && x.Role != AdminRole.None )
					.ToDictionary(
						x => (long)x.SteamId,
						x => new Entry
						{
							DisplayName = x.SteamId.ToString(),
							Role = x.Role
						} );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[AdminSystem] Failed to read '{StoragePath}' as a role map." );
		}

		try
		{
			var superAdmins = Json.Deserialize<List<long>>( _lastLoadedStorageText );
			if ( superAdmins is not null )
			{
				return superAdmins
					.Where( x => x > 0 )
					.Distinct()
					.ToDictionary(
						x => x,
						x => new Entry
						{
							DisplayName = x.ToString(),
							Role = AdminRole.SuperAdmin
						} );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[AdminSystem] Failed to read '{StoragePath}' as a SteamID list." );
		}

		try
		{
			var legacyEntries = Json.Deserialize<Dictionary<long, Entry>>( _lastLoadedStorageText );
			if ( legacyEntries is not null )
			{
				return legacyEntries;
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"[AdminSystem] Failed to read '{StoragePath}' as a legacy admin file." );
		}

		return new Dictionary<long, Entry>();
	}

	void RefreshFromStorageFileIfChanged()
	{
		if ( !_loaded || !Networking.IsHost )
			return;

		var currentText = FileSystem.Data.FileExists( StoragePath )
			? FileSystem.Data.ReadAllText( StoragePath )
			: null;

		if ( string.Equals( currentText, _lastLoadedStorageText, StringComparison.Ordinal ) )
			return;

		_entries = ReadStorageFile();

		foreach ( var connection in Connection.All )
		{
			RefreshPlayerRole( Player.FindForConnection( connection ) );
		}
	}

	static bool TryParseRole( string roleText, out AdminRole role )
	{
		role = ParseStoredRole( roleText );
		return roleText is not null && (role != AdminRole.None || roleText.Trim().Equals( "none", StringComparison.OrdinalIgnoreCase ) || roleText.Trim().Equals( "remove", StringComparison.OrdinalIgnoreCase ) || roleText.Trim().Equals( "user", StringComparison.OrdinalIgnoreCase ));
	}

	static AdminRole ParseStoredRole( string roleText )
	{
		switch ( roleText?.Trim().ToLowerInvariant() )
		{
			case "admin":
				return AdminRole.Admin;
			case "superadmin":
			case "super":
			case "owner":
				return AdminRole.SuperAdmin;
			default:
				return AdminRole.None;
		}
	}

	[ConCmd( "setadmin", ConVarFlags.Server, Help = "Set a DarkRP admin role by SteamID. Usage: setadmin <steamid> <none|admin|superadmin>" )]
	public static void SetAdminCommand( string steamIdText, string roleText )
	{
		if ( !Networking.IsHost || Current is null )
			return;

		if ( !ulong.TryParse( steamIdText, out var steamIdValue ) || steamIdValue == 0 )
		{
			Log.Warning( "Usage: setadmin <steamid> <none|admin|superadmin>" );
			return;
		}

		if ( !TryParseRole( roleText, out var role ) )
		{
			Log.Warning( $"Unknown admin role '{roleText}'. Use none, admin or superadmin." );
			return;
		}

		var steamId = (SteamId)steamIdValue;
		var connection = Connection.All.FirstOrDefault( x => x.SteamId == steamId );
		var displayName = connection?.DisplayName ?? steamIdValue.ToString();

		Current.SetRole( steamId, role, displayName );
		Log.Info( $"DarkRP admin role for {displayName} ({steamIdValue}) set to {role}." );
	}

	[ConCmd( "admin_delete", ConVarFlags.Server, Help = "Remove a DarkRP admin by SteamID. Usage: admin_delete <steamid>" )]
	public static void DeleteAdminCommand( string steamIdText )
	{
		if ( !Networking.IsHost || Current is null )
			return;

		if ( !ulong.TryParse( steamIdText, out var steamIdValue ) || steamIdValue == 0 )
		{
			Log.Warning( "Usage: admin_delete <steamid>" );
			return;
		}

		var steamId = (SteamId)steamIdValue;
		Current.SetRole( steamId, AdminRole.None, steamIdValue.ToString() );
		Log.Info( $"DarkRP admin role removed for {steamIdValue}." );
	}
}
