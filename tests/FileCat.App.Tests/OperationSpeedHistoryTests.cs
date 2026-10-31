using Avalonia;
using FileCat.App.Controls;
using FileCat.App.ViewModels;

namespace FileCat.App.Tests;

public sealed class OperationSpeedHistoryTests
{
    [Fact]
    public void A_stall_and_pause_occupy_time_at_zero_speed()
    {
        var history = new OperationSpeedHistory();
        Assert.False(history.Sample(TimeSpan.Zero, 0, running: true));
        Assert.False(history.HasTrend);
        Assert.True(history.Sample(TimeSpan.FromSeconds(1), 100, running: true));
        Assert.False(history.HasTrend); // One point has a rate but no history shape to show.
        Assert.True(history.Sample(TimeSpan.FromSeconds(2), 100, running: true));
        Assert.True(history.HasTrend);
        Assert.True(history.Sample(TimeSpan.FromSeconds(3), 100, running: false));
        Assert.True(history.Sample(TimeSpan.FromSeconds(4), 200, running: true));
        Assert.Equal([100d, 0d, 0d, 100d], history.Samples.Select(point => point.Y).ToArray());
        Assert.Equal([1d, 2d, 3d, 4d], history.Samples.Select(point => point.X).ToArray());
        Assert.Equal(100, history.Peak);
        Assert.Equal(100, history.Current);

        var pixels = SpeedGraph.Project([.. history.Samples], 100, 50);
        Assert.Equal(4, pixels.Length);
        Assert.True(pixels[0].X < pixels[1].X && pixels[1].X < pixels[2].X && pixels[2].X < pixels[3].X);
        Assert.Equal(pixels[1].Y, pixels[2].Y); // A two-second zero-speed stretch, not one vertical line.
        Assert.True(pixels[1].Y > pixels[0].Y);
    }

    [Fact]
    public void The_graph_retains_only_recent_finite_samples()
    {
        var history = new OperationSpeedHistory();
        history.Sample(TimeSpan.Zero, 0, running: true);
        history.Sample(TimeSpan.FromSeconds(1), 100, running: true);
        history.Sample(TimeSpan.FromSeconds(100), 100, running: true);
        Assert.Single(history.Samples);
        Assert.Equal(100, history.Samples[0].X);
        Assert.True(history.HasActivity); // The graph still shows the 90-second stall.
        Assert.False(history.HasTrend); // One retained point is not a useful graph.
        Assert.Equal(0, history.Current);
        Assert.Empty(SpeedGraph.Project([new Point(double.NaN, 1)], 100, 50));
    }
}
