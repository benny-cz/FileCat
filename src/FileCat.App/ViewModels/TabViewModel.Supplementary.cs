using FileCat.Core.HiddenData;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

public sealed partial class TabViewModel
{
    public bool IsSupplementaryData => Location?.Scheme == Schemes.HiddenData;
    public bool CanReadSupplementaryEas => IsSupplementaryData && !IsLoading &&
        Listing.Provider is HiddenDataProvider { Data.SupportsEaValueQuery: true };
    public string SupplementaryInventoryText
    {
        get
        {
            if (!IsSupplementaryData) return string.Empty;
            if (Listing.SupplementaryInventory is not { } inventory)
                return Listing.State == ListingState.Loading ? "Inventory pending; values not read." : "Inventory unavailable; no empty-inventory claim.";
            string text = inventory.Description;
            if (inventory.Notice is { Length: > 0 } notice) text += "\n" + notice;
            text += $" · Observed {inventory.ObservedAt.ToLocalTime():T}";
            if (Listing.IsRefreshing || Listing.State == ListingState.Loading) text += " · Previous observation; refreshing…";
            return text;
        }
    }

    private void NotifySupplementaryInventory()
    {
        OnPropertyChanged(nameof(IsSupplementaryData));
        OnPropertyChanged(nameof(CanReadSupplementaryEas));
        OnPropertyChanged(nameof(SupplementaryInventoryText));
    }
}
