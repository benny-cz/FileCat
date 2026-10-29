namespace FileCat.Remote.Tests;

public sealed class RemoteListingBudgetTests
{
    [Fact]
    public void Aggregate_name_budget_accepts_its_boundary_and_refuses_one_more_byte()
    {
        var budget = new RemoteListingBudget();
        string name = new('x', 64 << 10);
        for (int i = 0; i < 64; i++) budget.Add(name);
        Assert.Equal(RemoteListingBudget.MaxNameBytes, budget.NameBytes);
        Assert.Throws<RemoteListingLimitException>(() => budget.Add("y"));
        Assert.Equal(64, budget.Entries);
    }

    [Fact]
    public void A_job_listing_takes_far_more_entries_than_a_folder_shown_and_still_stops()
    {
        // Review finding R-M9: a job must see a large folder whole, within a bound of its own.
        var shown = new RemoteListingBudget();
        var job = new RemoteListingBudget(job: true);
        for (int i = 0; i < RemoteListingBudget.MaxEntries; i++) shown.Add("n");
        Assert.Throws<RemoteListingLimitException>(() => shown.Add("n"));
        for (int i = 0; i < RemoteListingBudget.MaxJobEntries; i++) job.Add("n");
        var refused = Assert.Throws<RemoteListingLimitException>(() => job.Add("n"));
        Assert.Contains(RemoteListingBudget.MaxJobEntries.ToString("N0"), refused.Message, StringComparison.Ordinal);
        Assert.Equal(RemoteListingBudget.MaxJobEntries, job.Entries);
    }
}
