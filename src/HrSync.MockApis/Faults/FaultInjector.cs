namespace HrSync.MockApis.Faults;

public sealed class FaultInjector
{
    public static readonly IReadOnlyList<string> Targets = ["hris", "care"];

    private readonly Lock _lock = new();
    private readonly Dictionary<string, FaultRule> _rules = new();
    private readonly Dictionary<string, int> _remaining = new();

    public static bool IsValidTarget(string target) => Targets.Contains(target);

    public void Set(string target, FaultRule rule)
    {
        lock (_lock)
        {
            _rules[target] = rule;
            _remaining[target] = rule.FailNext;
        }
    }

    // Consumes one pending failure, if any. DelayMs applies whether or not the request fails.
    public FaultDecision Evaluate(string target)
    {
        lock (_lock)
        {
            if (!_rules.TryGetValue(target, out var rule))
                return new FaultDecision(0, null);

            if (_remaining[target] <= 0)
                return new FaultDecision(rule.DelayMs, null);

            _remaining[target]--;
            return new FaultDecision(rule.DelayMs, rule);
        }
    }

    public IReadOnlyDictionary<string, FaultStatus> Snapshot()
    {
        lock (_lock)
        {
            return Targets.ToDictionary(
                t => t,
                t => _rules.TryGetValue(t, out var rule)
                    ? new FaultStatus(rule, _remaining[t])
                    : new FaultStatus(new FaultRule(), 0));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _rules.Clear();
            _remaining.Clear();
        }
    }
}
