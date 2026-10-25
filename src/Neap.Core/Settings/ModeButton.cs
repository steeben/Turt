namespace Neap.Core.Settings;

/// <summary>A job the Mode button can be given.</summary>
/// <param name="Function">The value written to the headset, a key of <see cref="Registry.ModeButtonOptions"/>.</param>
/// <param name="Cycle">Which cycle through noise control the app turns noise cancellation's on and off into, if any.</param>
public readonly record struct ModeChoice(int Function, NoiseCycle Cycle);

/// <summary>The jobs the Mode button can be given, the headset's own and the app's.</summary>
/// <remarks>
/// The cycles through noise control are not functions the headset has. On
/// the headset they are noise cancellation on/off, and the app does the
/// rest; see <see cref="NoiseControl"/>. So they are offered next to that
/// one, and shown only while the headset's function is noise cancellation.
/// Edited firmware has the cycle as its own noise cancellation function, so
/// there the app's cycles are not offered at all.
/// </remarks>
public static class ModeButton
{
    /// <summary>The headset's function that toggles noise cancellation.</summary>
    public const int NoiseCancellation = 0;

    /// <summary>Gets every choice, in the order they are offered.</summary>
    public static IReadOnlyList<ModeChoice> Choices { get; } =
        Registry.ModeButtonOptions.Keys.Order()
            .SelectMany(function => function == NoiseCancellation
                ? Enum.GetValues<NoiseCycle>().Select(cycle => new ModeChoice(function, cycle))
                : new[] { new ModeChoice(function, NoiseCycle.None) })
            .ToList();

    /// <summary>Whether a choice is offered for the headset's firmware.</summary>
    /// <param name="choice">One of <see cref="Choices"/>.</param>
    /// <param name="headsetCycles">Whether the headset's own firmware runs the noise cycle.</param>
    public static bool Offered(ModeChoice choice, bool headsetCycles) => !(headsetCycles && choice.Cycle != NoiseCycle.None);

    /// <summary>The choice in effect, or null until the headset reports its function.</summary>
    public static ModeChoice? Shown(int? function, NoiseCycle cycle, bool headsetCycles) =>
        function is int f ? new ModeChoice(f, f == NoiseCancellation && !headsetCycles ? cycle : NoiseCycle.None) : null;

    /// <summary>The cycle two saved switches describe: whether the button cycles, and whether the cycle skips off.</summary>
    /// <remarks>
    /// Saved as two switches so that settings saved before there was a
    /// choice of cycle keep theirs.
    /// </remarks>
    public static NoiseCycle CycleOf(bool cycles, bool skipsOff) =>
        !cycles ? NoiseCycle.None : skipsOff ? NoiseCycle.SkippingOff : NoiseCycle.AllThree;
}
