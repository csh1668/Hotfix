using System;
using System.Diagnostics;

namespace Hotfix;

public sealed class ScriptLimit(TimeSpan duration)
{
    private readonly long _deadline = Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency);
    private volatile bool _abandoned;

    public bool Expired => _abandoned || Stopwatch.GetTimestamp() > _deadline;

    public void Abandon() => _abandoned = true;
}
