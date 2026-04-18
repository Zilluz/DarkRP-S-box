public sealed class MiscShopItemDefinition
{
	public MiscShopItemDefinition( string prefabPath, string title, int price, string description, string requiredJobDefinitionPath = null, string requiredJobTitle = null )
	{
		PrefabPath = prefabPath;
		Title = title;
		Price = price;
		Description = description;
		RequiredJobDefinitionPath = requiredJobDefinitionPath;
		RequiredJobTitle = requiredJobTitle;
	}

	public string PrefabPath { get; }
	public string Title { get; }
	public int Price { get; }
	public string Description { get; }
	public string RequiredJobDefinitionPath { get; }
	public string RequiredJobTitle { get; }
}

public static class MiscShopCatalog
{
	public const string HoboJobDefinitionPath = Player.HoboJobDefinitionPath;
	public const string MayorJobDefinitionPath = Player.MayorJobDefinitionPath;

	static readonly MiscShopItemDefinition[] Items =
	[
		new( TipJar.PrefabPath, "Tip Jar", 150, "Place a jar so other players can donate money to you.", HoboJobDefinitionPath, "Hobo" ),
		new( Lawboard.PrefabPath, "Lawboard", 250, "Place a public board that mirrors the mayor's city laws.", MayorJobDefinitionPath, "Mayor" )
	];

	public static IReadOnlyList<MiscShopItemDefinition> GetAll()
	{
		return Items;
	}

	public static MiscShopItemDefinition Get( string prefabPath )
	{
		if ( string.IsNullOrWhiteSpace( prefabPath ) )
			return null;

		return Items.FirstOrDefault( x => string.Equals( x.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase ) );
	}

	public static bool ShouldShowInShop( Player player, MiscShopItemDefinition item )
	{
		if ( item is null )
			return false;

		return MeetsJobRequirement( player, item );
	}

	public static bool CanPlayerBuy( Player player, string prefabPath, out string reason )
	{
		reason = null;

		var item = Get( prefabPath );
		if ( item is null )
		{
			reason = "Unknown item.";
			return false;
		}

		if ( MeetsJobRequirement( player, item ) )
			return true;

		if ( string.IsNullOrWhiteSpace( item.RequiredJobTitle ) )
		{
			reason = "Player unavailable.";
			return false;
		}

		reason = $"{item.RequiredJobTitle} only.";
		return false;
	}

	static bool MeetsJobRequirement( Player player, MiscShopItemDefinition item )
	{
		if ( item is null )
			return false;

		if ( string.IsNullOrWhiteSpace( item.RequiredJobDefinitionPath ) )
			return true;

		var job = player?.CurrentJobDefinition;
		if ( job is null )
			return false;

		if ( string.Equals( job.ResourcePath, item.RequiredJobDefinitionPath, StringComparison.OrdinalIgnoreCase ) )
			return true;

		return !string.IsNullOrWhiteSpace( item.RequiredJobTitle )
			&& string.Equals( job.Title, item.RequiredJobTitle, StringComparison.OrdinalIgnoreCase );
	}
}
