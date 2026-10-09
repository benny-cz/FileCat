using Avalonia.Headless.XUnit;

namespace FileCat.App.Tests;

/// <summary>Reachable native enumeration deadlines, not an attribution of earlier machine-dependent failures.</summary>
public sealed class NetworkPlaceDeadlineTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(12000)]
    public Task A_known_server_return_observes_a_ready_view_with_delayed_owned_history(int returnDelayMs) =>
        NetworkPlaceTests.ExerciseReturnAsync(output, returnDelayMs);
}
