using Avalonia.Controls;
using UE4SSInstaller.Services;

namespace UE4SSInstaller.Tests;

public sealed class HandheldLayoutTests
{
    [Fact]
    public void Windowed_keeps_the_desktop_layout()
    {
        Assert.False(HandheldLayout.Detect(WindowState.Normal, "").IsHandheld);
    }

    [Fact]
    public void Maximized_uses_the_handheld_layout()
    {
        Assert.True(HandheldLayout.Detect(WindowState.Maximized, "").IsHandheld);
    }

    [Fact]
    public void Fullscreen_uses_the_handheld_layout()
    {
        Assert.True(HandheldLayout.Detect(WindowState.FullScreen, "").IsHandheld);
    }

    [Fact]
    public void Env_override_wins()
    {
        Assert.True(HandheldLayout.Detect(WindowState.Normal, "handheld").IsHandheld);
        Assert.False(HandheldLayout.Detect(WindowState.Maximized, "desktop").IsHandheld);
    }

    [Theory]
    [InlineData(7.4)]
    [InlineData(8.8)]
    [InlineData(10.9)]
    public void Screens_smaller_than_an_11_inch_laptop_start_expanded(double inches)
    {
        Assert.True(HandheldLayout.ShouldForceExpanded(inches, ""));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13.3)]
    [InlineData(15.6)]
    public void Eleven_inch_and_larger_laptops_stay_windowed(double inches)
    {
        Assert.False(HandheldLayout.ShouldForceExpanded(inches, ""));
    }

    [Fact]
    public void Unknown_screen_size_stays_windowed()
    {
        Assert.False(HandheldLayout.ShouldForceExpanded(null, ""));
    }

    [Fact]
    public void Force_expanded_honors_env_override()
    {
        Assert.False(HandheldLayout.ShouldForceExpanded(7.4, "desktop"));
        Assert.True(HandheldLayout.ShouldForceExpanded(15.6, "handheld"));
    }
}
