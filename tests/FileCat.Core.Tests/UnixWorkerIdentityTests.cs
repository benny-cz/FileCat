using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// A Unix worker that parses hostile input must not keep root's rights. The root case itself cannot be produced
/// from an ordinary test process; it is exercised with the worker probe under sudo on the owned Linux and macOS
/// machines, and these controls fix the behaviour everywhere else.
/// </summary>
public sealed class UnixWorkerIdentityTests
{
    [Fact]
    public void An_ordinary_user_keeps_the_identity_it_already_has()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Unix worker identity.");
        var result = UnixWorkerIdentity.Drop();
        if (result.StartedPrivileged) Assert.Skip("This process is root; the drop is verified separately under sudo.");

        Assert.Null(result.Refusal);
        Assert.Equal(result.UserBefore, result.UserAfter);
        Assert.Equal(result.GroupBefore, result.GroupAfter);
        Assert.NotEqual(0u, result.UserAfter);
        Assert.Equal(result.UserBefore, result.EffectiveUserBefore);
        Assert.Equal(result.GroupBefore, result.EffectiveGroupBefore);
        Assert.Equal(result.UserAfter, result.EffectiveUserAfter);
        Assert.Equal(result.GroupAfter, result.EffectiveGroupAfter);
        Assert.True(result.Unprivileged);
    }

    [Fact]
    public void Windows_has_its_own_worker_token_rules()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) Assert.Skip("Not Unix.");
        // Nothing to drop and nothing to refuse: Windows restricts the worker's token instead (I493).
        Assert.Null(UnixWorkerIdentity.Drop().Refusal);
    }
}
