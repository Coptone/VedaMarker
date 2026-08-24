namespace VedaMarker.Core;

public static class ForsakenEncounterIds
{
    public const uint Territory = 1363;

    public const uint ForsakenAction = 47804;

    public const uint LightOfJudgmentAction = 47805;

    public const uint InventoryStatus = 5083;

    public const uint ShareStatus = 5084;

    public const uint SteelStatus = 5085;

    public const uint FanStatus = 5086;

    public static bool IsMechanicStatus(uint statusId) => statusId is
        InventoryStatus or ShareStatus or SteelStatus or FanStatus;

    public static ForsakenMechanic MechanicFromStatus(uint statusId) => statusId switch
    {
        ShareStatus => ForsakenMechanic.Share,
        SteelStatus => ForsakenMechanic.Steel,
        FanStatus => ForsakenMechanic.Fan,
        _ => ForsakenMechanic.Unknown,
    };
}
