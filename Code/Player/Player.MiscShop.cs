using Sandbox.UI;

public sealed partial class Player
{
	[Rpc.Host]
	public void RequestBuyMiscItem( string prefabPath )
	{
		var definition = MiscShopCatalog.Get( prefabPath );
		if ( Rpc.Caller != Network.Owner || definition is null )
			return;

		if ( !MiscShopCatalog.CanPlayerBuy( this, prefabPath, out var restrictionReason ) )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, restrictionReason, 3 );
			return;
		}

		if ( string.Equals( prefabPath, TipJar.PrefabPath, StringComparison.OrdinalIgnoreCase )
			&& TipJar.CountOwned( Network.Owner ) >= TipJar.MaxOwnedPerPlayer )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, $"You already own {TipJar.MaxOwnedPerPlayer} tip jar.", 3 );
			return;
		}

		if ( string.Equals( prefabPath, Lawboard.PrefabPath, StringComparison.OrdinalIgnoreCase )
			&& Lawboard.CountOwned( Network.Owner ) >= Lawboard.MaxOwnedPerPlayer )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, $"You already own {Lawboard.MaxOwnedPerPlayer} lawboard.", 3 );
			return;
		}

		if ( !TryTakeMoney( definition.Price ) )
		{
			Notices.SendNotice( Network.Owner, "block", Color.Red, "You don't have enough money.", 3 );
			return;
		}

		if ( string.Equals( prefabPath, TipJar.PrefabPath, StringComparison.OrdinalIgnoreCase )
			&& TipJar.TrySpawn( this ) )
		{
			Notices.SendNotice( Network.Owner, "$", Color.Green, $"{definition.Title} purchased.", 3 );
			return;
		}

		if ( string.Equals( prefabPath, Lawboard.PrefabPath, StringComparison.OrdinalIgnoreCase )
			&& Lawboard.TrySpawn( this ) )
		{
			Notices.SendNotice( Network.Owner, "$", Color.Green, $"{definition.Title} purchased.", 3 );
			return;
		}

		GiveMoney( definition.Price );
		Notices.SendNotice( Network.Owner, "block", Color.Red, "Unable to place that item right now.", 3 );
	}
}
