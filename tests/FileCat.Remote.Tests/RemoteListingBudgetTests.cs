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
}
