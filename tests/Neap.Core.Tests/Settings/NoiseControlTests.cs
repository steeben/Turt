using Neap.Core.Settings;

namespace Neap.Core.Tests.Settings;

public class NoiseControlTests
{
    private const int Anc = NoiseControl.AncKey, Level = NoiseControl.LevelKey;

    private static NoiseControl At(int anc, int level, int? blocking = 80, NoiseCycle cycle = NoiseCycle.None)
    {
        var noise = new NoiseControl(blocking) { Cycle = cycle };
        noise.Observe(anc, level);
        return noise;
    }

    [Theory]
    [InlineData(0, 0, NoiseMode.Off)]
    [InlineData(0, 80, NoiseMode.Off)]
    [InlineData(1, 0, NoiseMode.Transparency)]
    [InlineData(1, 1, NoiseMode.Cancelling)]
    [InlineData(1, 100, NoiseMode.Cancelling)]
    public void TheTwoKeysMakeThreeModes(int anc, int level, NoiseMode mode) =>
        Assert.Equal(mode, NoiseControl.Of(anc, level));

    [Fact]
    public void AModeIsUnknownUntilBothKeysAreReported()
    {
        Assert.Null(NoiseControl.Of(null, 50));
        Assert.Null(NoiseControl.Of(1, null));
        Assert.Null(new NoiseControl(null).Mode);
    }

    [Fact]
    public void TheCycleRunsOffThenCancellingThenTransparency()
    {
        Assert.Equal(NoiseMode.Cancelling, NoiseControl.Next(NoiseMode.Off, NoiseCycle.AllThree));
        Assert.Equal(NoiseMode.Transparency, NoiseControl.Next(NoiseMode.Cancelling, NoiseCycle.AllThree));
        Assert.Equal(NoiseMode.Off, NoiseControl.Next(NoiseMode.Transparency, NoiseCycle.AllThree));
    }

    [Fact]
    public void TheCycleSkippingOffRunsCancellingThenTransparency()
    {
        Assert.Equal(NoiseMode.Transparency, NoiseControl.Next(NoiseMode.Cancelling, NoiseCycle.SkippingOff));
        Assert.Equal(NoiseMode.Cancelling, NoiseControl.Next(NoiseMode.Transparency, NoiseCycle.SkippingOff));
        Assert.Equal(NoiseMode.Cancelling, NoiseControl.Next(NoiseMode.Off, NoiseCycle.SkippingOff));
    }

    [Fact]
    public void StockFirmwareShowsTheModesInTheAppsCycle() =>
        Assert.Equal([NoiseMode.Off, NoiseMode.Cancelling, NoiseMode.Transparency], NoiseControl.Order(headsetCycles: false));

    [Fact]
    public void EditedFirmwareShowsTheModesInTheHeadsetsCycle() =>
        Assert.Equal([NoiseMode.Off, NoiseMode.Transparency, NoiseMode.Cancelling], NoiseControl.Order(headsetCycles: true));

    [Fact]
    public void NoiseCancellationRemembersTheLevelItWasUsedAt()
    {
        var noise = At(1, 60);
        noise.Observe(1, 0);
        Assert.Equal(60, noise.Blocking);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(101)]
    public void WithNoLevelRememberedNoiseCancellationIsFull(int? blocking) =>
        Assert.Equal(NoiseControl.FullBlocking, new NoiseControl(blocking).Blocking);

    [Fact]
    public void TransparencyIsNoiseCancellationAtZero() =>
        Assert.Equal([new SettingWrite(Level, 0)], At(1, 80).Choose(NoiseMode.Transparency));

    [Fact]
    public void NoiseCancellationSetsItsLevelBeforeTurningOn() =>
        Assert.Equal([new SettingWrite(Level, 80), new SettingWrite(Anc, 1)], At(0, 0).Choose(NoiseMode.Cancelling));

    [Fact]
    public void OffPutsTheLevelBackForTheNextPress() =>
        Assert.Equal([new SettingWrite(Anc, 0), new SettingWrite(Level, 80)], At(1, 0).Choose(NoiseMode.Off));

    [Fact]
    public void AModeChosenHereIsNotTakenForAPress()
    {
        var noise = At(1, 80, cycle: NoiseCycle.AllThree);
        noise.Choose(NoiseMode.Off);
        Assert.Empty(noise.Observe(0, 80));
        Assert.Equal(NoiseMode.Off, noise.Mode);
    }

    [Fact]
    public void ChoosingTheModeAlreadyOnWritesNothing() =>
        Assert.Empty(At(1, 80).Choose(NoiseMode.Cancelling));

    [Fact]
    public void APressFromCancellingGoesOnToTransparency()
    {
        var noise = At(1, 80, cycle: NoiseCycle.AllThree);
        Assert.Equal([new SettingWrite(Level, 0), new SettingWrite(Anc, 1)], noise.Observe(0, 80));
        Assert.Equal(NoiseMode.Transparency, noise.Mode);
    }

    [Fact]
    public void APressFromTransparencyStaysOffAndPutsTheLevelBack()
    {
        var noise = At(1, 0, cycle: NoiseCycle.AllThree);
        Assert.Equal([new SettingWrite(Level, 80)], noise.Observe(0, 0));
        Assert.Equal(NoiseMode.Off, noise.Mode);
    }

    [Fact]
    public void APressFromOffLandsOnCancellingWithNothingToWrite()
    {
        var noise = At(0, 80, cycle: NoiseCycle.AllThree);
        Assert.Empty(noise.Observe(1, 80));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
    }

    [Fact]
    public void APressFromOffWithTheLevelLeftAtZeroPutsItBack() =>
        Assert.Equal([new SettingWrite(Level, 80)], At(0, 0, cycle: NoiseCycle.AllThree).Observe(1, 0));

    [Fact]
    public void SkippingOffAPressFromCancellingGoesOnToTransparency()
    {
        var noise = At(1, 80, cycle: NoiseCycle.SkippingOff);
        Assert.Equal([new SettingWrite(Level, 0), new SettingWrite(Anc, 1)], noise.Observe(0, 80));
        Assert.Equal(NoiseMode.Transparency, noise.Mode);
    }

    [Fact]
    public void SkippingOffAPressFromTransparencyGoesBackToCancellingAtItsLevel()
    {
        var noise = At(1, 0, cycle: NoiseCycle.SkippingOff);
        Assert.Equal([new SettingWrite(Level, 80), new SettingWrite(Anc, 1)], noise.Observe(0, 0));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
    }

    [Fact]
    public void SkippingOffAPressFromOffLandsOnCancelling()
    {
        var noise = At(0, 80, cycle: NoiseCycle.SkippingOff);
        Assert.Empty(noise.Observe(1, 80));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
    }

    [Fact]
    public void SkippingOffTheHeadsetConfirmingAStepIsNotAnotherPress()
    {
        var noise = At(1, 0, cycle: NoiseCycle.SkippingOff);
        noise.Observe(0, 0);
        Assert.Empty(noise.Observe(1, 80));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
    }

    [Fact]
    public void WithoutCyclingAPressIsLeftToTheHeadset()
    {
        var noise = At(1, 80);
        Assert.Empty(noise.Observe(0, 80));
        Assert.Equal(NoiseMode.Off, noise.Mode);
    }

    [Fact]
    public void WithEditedFirmwareEachStepOfTheHeadsetsCycleIsOnlyFollowed()
    {
        var noise = new NoiseControl(80) { Cycle = NoiseCycle.AllThree, HeadsetCycles = true };
        noise.Observe(0, 80);
        Assert.Empty(noise.Observe(1, 0));
        Assert.Equal(NoiseMode.Transparency, noise.Mode);
        Assert.Empty(noise.Observe(1, 100));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
        Assert.Empty(noise.Observe(0, 100));
        Assert.Equal(NoiseMode.Off, noise.Mode);
    }

    [Fact]
    public void TheFirstReadingIsNotAPress()
    {
        var noise = new NoiseControl(80) { Cycle = NoiseCycle.AllThree };
        Assert.Empty(noise.Observe(1, 80));
        Assert.Equal(NoiseMode.Cancelling, noise.Mode);
    }

    [Fact]
    public void AChangeOfLevelIsNotAPress() =>
        Assert.Empty(At(1, 80, cycle: NoiseCycle.AllThree).Observe(1, 40));

    [Fact]
    public void TheHeadsetConfirmingAStepIsNotAnotherPress()
    {
        var noise = At(1, 80, cycle: NoiseCycle.AllThree);
        noise.Observe(0, 80);
        Assert.Empty(noise.Observe(1, 0));
        Assert.Equal(NoiseMode.Transparency, noise.Mode);
    }

    [Fact]
    public void BlockingCanBeSetDirectly()
    {
        var noise = new NoiseControl(50);
        noise.SetBlocking(80);
        Assert.Equal(80, noise.Blocking);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    public void SetBlockingKeepsItInRange(int given)
    {
        var noise = new NoiseControl(50);
        noise.SetBlocking(given);
        Assert.InRange(noise.Blocking, NoiseControl.LeastBlocking, 100);
    }
}
