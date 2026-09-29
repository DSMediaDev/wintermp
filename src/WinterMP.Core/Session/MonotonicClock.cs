using System.Diagnostics;

namespace WinterMP.Core.Session;

/// <summary>
/// Milliseconds from a steady clock that never jumps with wall-clock changes. Sessions take time as
/// a parameter (so tests can drive it by hand); real hosts feed them this.
/// </summary>
public static class MonotonicClock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static long NowMs => Watch.ElapsedMilliseconds;
}
