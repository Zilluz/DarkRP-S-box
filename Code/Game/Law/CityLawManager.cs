using System.Text.Json;
using System.Text.RegularExpressions;
using Sandbox.UI;

public sealed class CityLawManager : Component
{
	public const int MaxLaws = 8;
	public const int MaxLawLength = 96;
	const string EmptyLawsJson = "[]";

	static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false
	};

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( OnLawsJsonChanged ) )]
	public string LawsJson { get; private set; } = EmptyLawsJson;

	[Property, Sync( SyncFlags.FromHost )]
	public int Revision { get; private set; }

	List<string> CachedLaws;

	public static CityLawManager Current => Game.ActiveScene?.Get<CityLawManager>();

	public IReadOnlyList<string> Laws => GetCachedLaws();

	public static CityLawManager Ensure( Scene scene )
	{
		if ( scene is null )
			return null;

		var existing = scene.Get<CityLawManager>();
		if ( existing.IsValid() )
			return existing;

		var go = new GameObject( true, "City Law Manager" );
		var manager = go.AddComponent<CityLawManager>();
		go.NetworkSpawn( null );
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
		return manager;
	}

	[Rpc.Host]
	public void RequestAddLaw( string lawText )
	{
		var mayor = Player.FindForConnection( Rpc.Caller );
		if ( !CanManageLaws( mayor ) )
			return;

		var law = SanitizeLaw( lawText );
		if ( string.IsNullOrWhiteSpace( law ) )
		{
			Notices.SendNotice( Rpc.Caller, "block", Color.Red, "Enter a city law first.", 3 );
			return;
		}

		var laws = GetCachedLaws().ToList();
		if ( laws.Count >= MaxLaws )
		{
			Notices.SendNotice( Rpc.Caller, "block", Color.Red, $"The city can only have {MaxLaws} laws.", 3 );
			return;
		}

		if ( laws.Any( x => string.Equals( x, law, StringComparison.OrdinalIgnoreCase ) ) )
		{
			Notices.SendNotice( Rpc.Caller, "block", Color.Red, "That city law already exists.", 3 );
			return;
		}

		laws.Add( law );
		SetLaws( laws );

		Notices.SendNotice( Rpc.Caller, "gavel", Color.Green, "City law added.", 3 );
		Scene.Get<Chat>()?.AddSystemText( $"Mayor {mayor.DisplayName} added a city law: {law}", "📜" );
	}

	[Rpc.Host]
	public void RequestRemoveLaw( int index )
	{
		var mayor = Player.FindForConnection( Rpc.Caller );
		if ( !CanManageLaws( mayor ) )
			return;

		var laws = GetCachedLaws().ToList();
		if ( index < 0 || index >= laws.Count )
			return;

		var removedLaw = laws[index];
		laws.RemoveAt( index );
		SetLaws( laws );

		Notices.SendNotice( Rpc.Caller, "gavel", Color.Green, "City law removed.", 3 );
		Scene.Get<Chat>()?.AddSystemText( $"Mayor {mayor.DisplayName} removed a city law: {removedLaw}", "📜" );
	}

	public void ClearLaws()
	{
		if ( !Networking.IsHost )
			return;

		SetLaws( [] );
	}

	void SetLaws( IReadOnlyList<string> laws )
	{
		if ( !Networking.IsHost )
			return;

		var cleanedLaws = laws
			.Select( SanitizeLaw )
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Take( MaxLaws )
			.ToList();

		CachedLaws = cleanedLaws;
		LawsJson = JsonSerializer.Serialize( cleanedLaws, JsonOptions );
		Revision++;
	}

	void OnLawsJsonChanged( string oldValue, string newValue )
	{
		CachedLaws = null;
	}

	List<string> GetCachedLaws()
	{
		if ( CachedLaws is not null )
			return CachedLaws;

		CachedLaws = ParseLaws( LawsJson );
		return CachedLaws;
	}

	static List<string> ParseLaws( string lawsJson )
	{
		if ( string.IsNullOrWhiteSpace( lawsJson ) )
			return [];

		try
		{
			return (JsonSerializer.Deserialize<List<string>>( lawsJson ) ?? [])
				.Select( SanitizeLaw )
				.Where( x => !string.IsNullOrWhiteSpace( x ) )
				.Take( MaxLaws )
				.ToList();
		}
		catch ( JsonException )
		{
			return [];
		}
	}

	static string SanitizeLaw( string lawText )
	{
		var law = Regex.Replace( lawText ?? string.Empty, @"\s+", " " ).Trim();
		if ( law.Length <= MaxLawLength )
			return law;

		return law[..MaxLawLength].Trim();
	}

	static bool CanManageLaws( Player player )
	{
		return player.IsValid() && player.IsMayor;
	}
}
