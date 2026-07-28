namespace BOCCHI.ItemHelpers;

public static class Items
{
    private static readonly Item SouthSilver = new(45043);

    private static readonly Item SouthGold = new(45044);

    private static readonly Item NorthSilver = new(51975);

    private static readonly Item NorthGold = new(51976);

    public static Item Silver
    {
        get => BOCCHI.Data.ZoneData.IsInNorthHorn() ? NorthSilver : SouthSilver;
    }

    public static Item Gold
    {
        get => BOCCHI.Data.ZoneData.IsInNorthHorn() ? NorthGold : SouthGold;
    }

    public static Item FortuneCarrot { get; private set; } = new(48096);
}
