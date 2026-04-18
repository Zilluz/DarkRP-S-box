using Sandbox.UI;

public sealed class TipJar : Component, Component.IPressable
{
	public const string PrefabPath = "entities/misc/tip_jar.prefab";
	public const int MaxOwnedPerPlayer = 1;
	public const int DefaultDonationAmount = 25;
	public const int MaxDonationAmount = 10000;

	List<TextRenderer> Labels = new();

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( OnStoredMoneyChanged ) )]
	public int StoredMoney { get; set; }

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		if ( !player.IsValid() )
			return null;

		if ( IsOwner( player ) )
		{
			var description = StoredMoney > 0
				? $"Tip Jar - ${StoredMoney:n0}"
				: "Tip Jar - Empty";

			return new IPressable.Tooltip( "Collect Tips", "$", description );
		}

		var ownerName = GetOwnerName();
		return new IPressable.Tooltip( "Donate", "$", $"Donate to {ownerName}'s tip jar." );
	}

	bool IPressable.CanPress( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		if ( !player.IsValid() )
			return false;

		if ( IsOwner( player ) )
			return StoredMoney > 0;

		return GetOwnerConnection() is not null && player.Money > 0;
	}

	bool IPressable.Press( IPressable.Event e )
	{
		var player = GetPlayer( e.Source.GameObject );
		if ( !player.IsValid() )
			return false;

		if ( IsOwner( player ) )
		{
			CollectDonations( player.GameObject );
			return true;
		}

		OpenDonationPopup( player.Network.Owner, GetOwnerName() );
		return true;
	}

	protected override void OnStart()
	{
		CacheLabels();
		RefreshLabels();
	}

	protected override void OnUpdate()
	{
		if ( Labels.Count == 0 )
			CacheLabels();

		RefreshLabels();
	}

	void OpenDonationPopup( Connection donor, string ownerName )
	{
		if ( donor is null )
			return;

		using ( Rpc.FilterInclude( donor ) )
		{
			OpenDonationPopupClient( ownerName );
		}
	}

	[Rpc.Broadcast]
	void OpenDonationPopupClient( string ownerName )
	{
		if ( Application.IsDedicatedServer )
			return;

		TipJarDonationPopup.Open( this, ownerName );
	}

	[Rpc.Host]
	public void RequestDonate( int amount )
	{
		if ( GameObject.IsDestroyed || amount <= 0 || amount > MaxDonationAmount )
			return;

		var donor = Player.FindForConnection( Rpc.Caller );
		if ( !donor.IsValid() )
			return;

		if ( IsOwner( donor ) )
			return;

		var ownerConnection = GetOwnerConnection();
		var owner = Player.FindForConnection( ownerConnection );
		if ( ownerConnection is null || !owner.IsValid() )
		{
			Notices.SendNotice( donor.Network.Owner, "block", Color.Red, "The owner is not available.", 3 );
			return;
		}

		if ( !donor.TryTakeMoney( amount ) )
		{
			Notices.SendNotice( donor.Network.Owner, "block", Color.Red, "You don't have enough money.", 3 );
			return;
		}

		StoredMoney += amount;
		RefreshLabels();

		Notices.SendNotice( donor.Network.Owner, "$", Color.Green, $"Donated ${amount:n0} to {owner.DisplayName}.", 3 );
		Notices.SendNotice( ownerConnection, "$", Color.Green, $"{donor.DisplayName} donated ${amount:n0} to your tip jar.", 3 );
		PlayDonationEffects();
	}

	[Rpc.Host]
	void CollectDonations( GameObject collectorObject )
	{
		if ( GameObject.IsDestroyed || StoredMoney <= 0 )
			return;

		var collector = GetPlayer( collectorObject );
		if ( !collector.IsValid() || Rpc.Caller != collector.Network.Owner || !IsOwner( collector ) )
			return;

		var collected = StoredMoney;
		StoredMoney = 0;
		RefreshLabels();

		collector.GiveMoney( collected );
		Notices.SendNotice( collector.Network.Owner, "$", Color.Green, $"Collected ${collected:n0} from your tip jar.", 3 );
		PlayDonationEffects();
	}

	[Rpc.Broadcast]
	void PlayDonationEffects()
	{
		if ( Application.IsDedicatedServer )
			return;

		Sound.Play( "sounds/ui/ui.spawn.sound", WorldPosition );
	}

	void OnStoredMoneyChanged( int oldAmount, int newAmount )
	{
		RefreshLabels();
	}

	void CacheLabels()
	{
		Labels = EnumerateSelfAndDescendants( GameObject )
			.SelectMany( x => x.Components.GetAll<TextRenderer>() )
			.Where( x => x.IsValid() )
			.ToList();
	}

	void RefreshLabels()
	{
		var labelText = StoredMoney > 0 ? $"${MoneyFormatter.FormatCompact( StoredMoney )}" : "Tips";

		foreach ( var label in Labels )
		{
			if ( !label.IsValid() )
				continue;

			var textScope = label.TextScope;
			if ( textScope.Text == labelText )
				continue;

			textScope.Text = labelText;
			label.TextScope = textScope;
		}
	}

	bool IsOwner( Player player )
	{
		return player.IsValid() && player.Network.Owner == GetOwnerConnection();
	}

	Connection GetOwnerConnection()
	{
		return GameObject.GetComponent<Ownable>()?.Owner;
	}

	string GetOwnerName()
	{
		var ownerConnection = GetOwnerConnection();
		return Player.FindForConnection( ownerConnection )?.DisplayName
			?? ownerConnection?.DisplayName
			?? "Unknown";
	}

	static Player GetPlayer( GameObject source )
	{
		return source?.Root.GetComponent<Player>();
	}

	public static int CountOwned( Connection owner )
	{
		if ( owner is null || Game.ActiveScene is null )
			return 0;

		return Game.ActiveScene.GetAllComponents<TipJar>()
			.Count( x => x.IsValid() && x.GameObject.GetComponent<Ownable>()?.Owner == owner );
	}

	public static int DestroyOwned( Connection owner )
	{
		if ( !Networking.IsHost || owner is null || Game.ActiveScene is null )
			return 0;

		var ownedTipJars = Game.ActiveScene.GetAllComponents<TipJar>()
			.Where( x => x.IsValid() && x.GameObject.GetComponent<Ownable>()?.Owner == owner )
			.ToArray();

		foreach ( var tipJar in ownedTipJars )
		{
			tipJar.GameObject.Destroy();
		}

		return ownedTipJars.Length;
	}

	public static bool TrySpawn( Player owner )
	{
		if ( !Networking.IsHost || !owner.IsValid() )
			return false;

		var prefab = GameObject.GetPrefab( PrefabPath );
		if ( prefab is null )
		{
			Log.Warning( $"Prefab not found: {PrefabPath}" );
			return false;
		}

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
		spawnTransform.Position += spawnTransform.Up * 1.0f;

		var dropped = prefab.Clone( new CloneConfig
		{
			Transform = spawnTransform,
			StartEnabled = false
		} );

		var tipJar = dropped.GetComponent<TipJar>( true );
		if ( !tipJar.IsValid() )
		{
			dropped.Destroy();
			return false;
		}

		tipJar.StoredMoney = 0;
		tipJar.CacheLabels();
		tipJar.RefreshLabels();

		dropped.Tags.Add( "removable" );
		Ownable.Set( dropped, owner.Network.Owner );
		dropped.NetworkSpawn();

		if ( dropped.GetComponent<Rigidbody>() is { } rb )
		{
			rb.Velocity = owner.Controller.Velocity;
		}

		return true;
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
