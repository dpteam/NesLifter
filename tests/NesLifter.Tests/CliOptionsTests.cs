using NesLifter.Compilation;
using NesLifter.Host;
using Xunit;

namespace NesLifter.Tests;

public class CliOptionsTests
{
    [Fact]
    public void NoArgumentsMeansNoInput()
    {
        CliOptions options = CliOptions.Parse(Array.Empty<string>());

        Assert.Equal(string.Empty, options.InputPath);
        Assert.False(options.Gui);
        Assert.False(options.NoTui);
        Assert.False(options.RunSelfTest);
    }

    [Fact]
    public void BarePathBecomesInput()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes" });

        Assert.EndsWith("game.nes", options.InputPath);
        Assert.Empty(options.UnknownArgs);
    }

    [Fact]
    public void GuiFlagIsParsed()
    {
        CliOptions options = CliOptions.Parse(new[] { "--gui" });

        Assert.True(options.Gui);
    }

    [Theory]
    [InlineData("--no-tui")]
    [InlineData("--cli")]
    public void NoTuiFlagsAreParsed(string flag)
    {
        CliOptions options = CliOptions.Parse(new[] { flag });

        Assert.True(options.NoTui);
    }

    [Fact]
    public void PackageModeDefaultsToAppHost()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes" });

        Assert.Equal(PackageMode.AppHost, options.PackageMode);
    }

    [Fact]
    public void DllFlagSelectsAssemblyOnly()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--dll" });

        Assert.Equal(PackageMode.AssemblyOnly, options.PackageMode);
    }

    [Fact]
    public void ExeFlagSelectsAppHost()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--dll", "--exe" });

        Assert.Equal(PackageMode.AppHost, options.PackageMode);
    }

    [Theory]
    [InlineData("--full-exe")]
    [InlineData("--self-contained")]
    [InlineData("--standalone")]
    public void SelfContainedAliasesSelectFullExe(string flag)
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", flag });

        Assert.Equal(PackageMode.SelfContained, options.PackageMode);
    }

    [Fact]
    public void IconPathIsParsed()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--icon", "cover.png" });

        Assert.Equal("cover.png", options.IconPath);
    }

    [Fact]
    public void IconPathEqualsFormIsParsed()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--icon=cover.png" });

        Assert.Equal("cover.png", options.IconPath);
    }

    [Fact]
    public void CaptureIconSecondsIsParsed()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--capture-icon", "12" });

        Assert.Equal(12, options.CaptureIconSeconds);
    }

    [Fact]
    public void NoIconDisablesCapture()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--no-icon" });

        Assert.Equal(0, options.CaptureIconSeconds);
    }

    [Fact]
    public void RecursiveFlagIsTrackedAsExplicit()
    {
        CliOptions plain = CliOptions.Parse(new[] { "roms" });
        Assert.False(plain.RecursiveSpecified);

        CliOptions explicitRecursive = CliOptions.Parse(new[] { "roms", "-r" });
        Assert.True(explicitRecursive.RecursiveSpecified);
        Assert.True(explicitRecursive.Recursive);
    }

    [Fact]
    public void UnknownArgumentIsReported()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "--nope" });

        Assert.Contains("--nope", options.UnknownArgs);
    }

    [Fact]
    public void OutputPathIsMadeAbsolute()
    {
        CliOptions options = CliOptions.Parse(new[] { "game.nes", "-o", "out" });

        Assert.True(Path.IsPathRooted(options.OutputPath));
    }

    [Fact]
    public void SelfTestFlagIsParsed()
    {
        CliOptions options = CliOptions.Parse(new[] { "--selftest", "--frames", "60" });

        Assert.True(options.RunSelfTest);
        Assert.Equal(60, options.HeadlessFrames);
    }
}
