using Sandbox.UI;

public sealed class Lawboard : Component, Component.IPressable
{
	public const string PrefabPath = "entities/misc/lawboard.prefab";
	public const int MaxOwnedPerPlayer = 1;

	static readonly Dictionary<string, BBox> CachedBounds = new();

	List<TextRenderer> Labels = new();
	int _lastRevision = -1;
	string _lastBoardText;

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var laws = CityLawManager.Current?.Laws;
		var description = laws is { Count: > 0 }
			? $"{laws.Count} city law{(laws.Count == 1 ? "" : "s")} posted."
			: "No city laws posted.";

		return new IPressable.Tooltip( "City Laws", "gavel", description );
	}

	bool IPressable.CanPress( IPressable.Event e ) => false;
	bool IPressable.Press( IPressable.Event e ) => false;

	protected override void OnStart()
	{
		CacheLabels();
		RefreshLabels( true );
	}

	protected override void OnUpdate()
	{
		if ( Labels.Count == 0 )
			CacheLabels();

		RefreshLabels( false );
	}

	void RefreshLabels( bool force )
	{
		var manager = CityLawManager.Current;
		var revision = manager?.Revision ?? 0;
		if ( !force && revision == _lastRevision )
			return;

		_lastRevision = revision;
		var boardText = BuildBoardText( manager?.Laws ?? [] );
		if ( !force && string.Equals( _lastBoardText, boardText, StringComparison.Ordinal ) )
			return;

		_lastBoardText = boardText;
		foreach ( var label in Labels )
		{
			if ( !label.IsValid() )
				continue;

			var textScope = label.TextScope;
			textScope.Text = boardText;
			label.TextScope = textScope;
		}
	}

	static string BuildBoardText( IReadOnlyList<string> laws )
	{
		if ( laws is null || laws.Count == 0 )
			return "CITY LAWS\n\nNo laws posted.";

		var lines = new List<string> { "CITY LAWS", "" };
		for ( var i = 0; i < laws.Count; i++ )
		{
			var wrapped = WrapLaw( $"{i + 1}. {laws[i]}", 34 );
			lines.AddRange( wrapped );
		}

		return string.Join( "\n", lines );
	}

	static IEnumerable<string> WrapLaw( string text, int maxLineLength )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			yield break;

		var words = text.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var line = "";

		foreach ( var word in words )
		{
			if ( line.Length == 0 )
			{
				line = word;
				continue;
			}

			if ( line.Length + word.Length + 1 <= maxLineLength )
			{
				line += " " + word;
				continue;
			}

			yield return line;
			line = "   " + word;
		}

		if ( line.Length > 0 )
			yield return line;
	}

	void CacheLabels()
	{
		Labels = EnumerateSelfAndDescendants( GameObject )
			.SelectMany( x => x.Components.GetAll<TextRenderer>() )
			.Where( x => x.IsValid() )
			.ToList();
	}

	public static int CountOwned( Connection owner )
	{
		if ( owner is null || Game.ActiveScene is null )
			return 0;

		return Game.ActiveScene.GetAllComponents<Lawboard>()
			.Count( x => x.IsValid() && x.GameObject.GetComponent<Ownable>()?.Owner == owner );
	}

	public static int DestroyOwned( Connection owner )
	{
		if ( !Networking.IsHost || owner is null || Game.ActiveScene is null )
			return 0;

		var ownedLawboards = Game.ActiveScene.GetAllComponents<Lawboard>()
			.Where( x => x.IsValid() && x.GameObject.GetComponent<Ownable>()?.Owner == owner )
			.ToArray();

		foreach ( var lawboard in ownedLawboards )
		{
			lawboard.GameObject.Destroy();
		}

		return ownedLawboards.Length;
	}

	public static bool TrySpawn( Player owner )
	{
		if ( !Networking.IsHost || !owner.IsValid() )
			return false;

		CityLawManager.Ensure( Game.ActiveScene );

		var prefab = GameObject.GetPrefab( PrefabPath );
		if ( prefab is null )
		{
			Log.Warning( $"Prefab not found: {PrefabPath}" );
			return false;
		}

		var bounds = GetSpawnBounds( prefab );
		var eyes = owner.EyeTransform;
		var trace = Game.SceneTrace.Ray( eyes.Position, eyes.Position + eyes.Forward * 200.0f )
			.IgnoreGameObject( owner.GameObject )
			.WithoutTags( "player" )
			.Run();

		var up = trace.Normal.Length.AlmostEqual( 0.0f ) ? Vector3.Up : trace.Normal;
		var backward = -eyes.Forward;
		var right = Vector3.Cross( up, backward ).Normal;
		var forward = Vector3.Cross( right, up ).Normal;

		if ( forward.Length.AlmostEqual( 0.0f ) )
			forward = owner.WorldRotation.Forward.WithZ( 0 ).Normal;

		var spawnTransform = new Transform( trace.EndPosition, Rotation.LookAt( forward, up ) );
		spawnTransform.Position += spawnTransform.Up * -bounds.Mins.z;
		spawnTransform.Rotation *= Rotation.FromYaw( GetYawCorrection( bounds ) );

		var lawboardObject = prefab.Clone( new CloneConfig
		{
			Transform = spawnTransform,
			StartEnabled = false
		} );

		var lawboard = lawboardObject.GetComponent<Lawboard>( true );
		if ( !lawboard.IsValid() )
		{
			lawboardObject.Destroy();
			return false;
		}

		lawboard.CacheLabels();
		lawboard.RefreshLabels( true );

		lawboardObject.Tags.Add( "removable" );
		Ownable.Set( lawboardObject, owner.Network.Owner );
		lawboardObject.NetworkSpawn();

		if ( lawboardObject.GetComponent<Rigidbody>() is { } rb )
		{
			rb.Velocity = owner.Controller.Velocity;
		}

		return true;
	}

	static BBox GetSpawnBounds( GameObject prefab )
	{
		var resourcePath = prefab.PrefabInstanceSource ?? PrefabPath;
		if ( CachedBounds.TryGetValue( resourcePath, out var bounds ) )
			return bounds;

		bounds = prefab.GetBounds();
		CachedBounds[resourcePath] = bounds;
		return bounds;
	}

	static float GetYawCorrection( BBox bounds )
	{
		var size = bounds.Size;
		return size.x > size.y ? 90.0f : 0.0f;
	}

	static IEnumerable<GameObject> EnumerateSelfAndDescendants( GameObject root )
	{
		yield return root;

		foreach ( var child in root.Children )
		{
			foreach ( var nested in EnumerateSelfAndDescendants( child ) )
			{
				yield return nested;
			}
		}
	}
}
