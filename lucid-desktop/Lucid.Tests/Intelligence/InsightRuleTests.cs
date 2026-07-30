using FluentAssertions;
using Lucid.Helpers;
using Lucid.Services;
using Lucid.Services.Intelligence;
using Lucid.Services.Intelligence.Rules;
using Lucid.Services.Telemetry;
using Xunit;

namespace Lucid.Tests.Intelligence;

/// <summary>
/// Behavioural tests for the registered insight rules.
///
/// Rules are the product's reasoning layer and are pure functions of
/// (<see cref="TelemetrySnapshot"/>, <see cref="ITelemetryHistoryBuffer"/>) —
/// no I/O, no clock, no statics — so they can be driven directly. These tests
/// assert three things per rule:
///
///   1. It fires when its condition is met.
///   2. It stays silent when the condition is absent, when data is missing,
///      and when a sensor reports "unavailable" (the cold-start contract).
///   3. Its wording honours the security-language doctrine — findings are
///      confidence-aware and descriptive, never alarmist.
///
/// See <see cref="IInsightRule"/> for the contract these verify.
/// </summary>
public class InsightRuleTests
{
    // ── Test doubles ──────────────────────────────────────────────────────────

    /// <summary>
    /// History buffer stub returning caller-supplied stats per metric.
    /// Rules only read through GetStats/GetSamples, so this is sufficient.
    /// </summary>
    private sealed class StubHistory : ITelemetryHistoryBuffer
    {
        private readonly Dictionary<TelemetryMetric, MetricStats> _stats = [];

        public StubHistory With(TelemetryMetric metric, double average, int sampleCount,
                                double min = 0, double max = 100, double latest = 0)
        {
            _stats[metric] = new MetricStats(min, max, average, latest, sampleCount);
            return this;
        }

        public int Capacity => 1200;
        public int Count    => _stats.Values.Select(s => s.SampleCount).DefaultIfEmpty(0).Max();

        public void Record(TelemetrySnapshot snapshot) { }

        public IReadOnlyList<MetricPoint> GetSamples(TelemetryMetric metric, TimeWindow window) => [];

        public MetricStats GetStats(TelemetryMetric metric, TimeWindow window) =>
            _stats.TryGetValue(metric, out var s) ? s : default;
    }

    /// <summary>Empty history — models the cold-start case before any samples land.</summary>
    private static StubHistory NoHistory() => new();

    /// <summary>
    /// A snapshot describing a healthy machine. Individual tests override only
    /// the fields under test, so each test reads as one deviation from normal.
    /// </summary>
    private static TelemetrySnapshot Healthy(
        double cpu          = 10,
        double ram          = 40,
        double disk         = 50,
        double diskTotalGb  = 500,
        double diskUsedGb   = 250,
        double gpu          = 10,
        double tempC        = 45,
        bool   tempAvail    = true) =>
        new(CpuPercent:              cpu,
            CpuFrequencyGhz:         3.5,
            CpuCoreCount:            8,
            RamPercent:              ram,
            RamUsedGb:               6,
            RamTotalGb:              16,
            DiskPercent:             disk,
            DiskUsedGb:              diskUsedGb,
            DiskTotalGb:             diskTotalGb,
            GpuPercent:              gpu,
            GpuAvailable:            true,
            CpuTemperatureCelsius:   tempC,
            CpuTemperatureAvailable: tempAvail);

    // ═══════════════════ LowDiskSpaceRule — direct sensor ═══════════════════

    [Theory]
    [InlineData(91.0)]
    [InlineData(95.0)]
    [InlineData(99.5)]
    public void LowDiskSpace_fires_above_the_threshold(double diskPercent)
    {
        var rule = new LowDiskSpaceRule();

        var insight = rule.Evaluate(Healthy(disk: diskPercent), NoHistory());

        insight.Should().NotBeNull();
        insight!.Id.Should().Be("disk.low-space");
    }

    [Theory]
    [InlineData(50.0)]
    [InlineData(89.9)]
    [InlineData(90.0)]   // boundary is exclusive — 90% exactly must stay silent
    public void LowDiskSpace_is_silent_at_or_below_the_threshold(double diskPercent)
    {
        var rule = new LowDiskSpaceRule();

        rule.Evaluate(Healthy(disk: diskPercent), NoHistory()).Should().BeNull();
    }

    [Fact]
    public void LowDiskSpace_is_silent_when_disk_size_is_unknown()
    {
        // DiskTotalGb == 0 means the sampler has no reading yet. A rule must not
        // infer "disk full" from absent data.
        var rule = new LowDiskSpaceRule();

        rule.Evaluate(Healthy(disk: 99, diskTotalGb: 0), NoHistory()).Should().BeNull();
    }

    [Fact]
    public void LowDiskSpace_escalates_severity_when_critically_full()
    {
        var rule = new LowDiskSpaceRule();

        var warning  = rule.Evaluate(Healthy(disk: 91), NoHistory())!;
        var critical = rule.Evaluate(Healthy(disk: 96), NoHistory())!;

        ((int)critical.Severity).Should().BeGreaterThan((int)warning.Severity,
            "a fuller disk must not report a lower severity than a less-full one");
    }

    [Fact]
    public void LowDiskSpace_offers_reversible_actions_first()
    {
        var rule = new LowDiskSpaceRule();

        var insight = rule.Evaluate(Healthy(disk: 96), NoHistory())!;

        insight.RecommendedActions.Should().NotBeEmpty();
        insight.RecommendedActions[0].IsReversible.Should().BeTrue(
            "the least destructive remedy should be the first one offered");
        insight.RecommendedActions
            .Where(a => !a.IsReversible)
            .Should().OnlyContain(a => a.RequiresConfirmation,
                "an irreversible action must always be gated behind confirmation");
    }

    // ═══════════════ HighCpuTemperatureRule — sensor availability ═══════════════

    [Fact]
    public void HighCpuTemperature_is_silent_when_the_sensor_is_unavailable()
    {
        // Machines without an ACPI thermal zone report 0 °C with the flag false.
        // Treating that as "cold" is fine; treating it as a reading is not.
        var rule = new HighCpuTemperatureRule();

        rule.Evaluate(Healthy(tempC: 95, tempAvail: false), NoHistory())
            .Should().BeNull("an unavailable sensor must never produce a finding");
    }

    [Fact]
    public void HighCpuTemperature_fires_when_the_sensor_reports_a_hot_cpu()
    {
        var rule = new HighCpuTemperatureRule();

        var insight = rule.Evaluate(Healthy(tempC: 85, tempAvail: true), NoHistory());

        insight.Should().NotBeNull();
        insight!.Id.Should().Be("cpu.high-temperature");
    }

    [Theory]
    [InlineData(60.0)]
    [InlineData(80.0)]   // boundary is exclusive
    public void HighCpuTemperature_is_silent_at_safe_temperatures(double tempC)
    {
        var rule = new HighCpuTemperatureRule();

        rule.Evaluate(Healthy(tempC: tempC, tempAvail: true), NoHistory()).Should().BeNull();
    }

    // ═══════════════ SustainedHighCpuRule — history-gated ═══════════════

    [Fact]
    public void SustainedHighCpu_is_silent_without_history()
    {
        // Cold start: the rule must not fire off a single spike.
        var rule = new SustainedHighCpuRule();

        rule.Evaluate(Healthy(cpu: 99), NoHistory()).Should().BeNull();
    }

    [Fact]
    public void SustainedHighCpu_is_silent_below_the_minimum_sample_count()
    {
        var rule    = new SustainedHighCpuRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 95, sampleCount: 9);

        rule.Evaluate(Healthy(cpu: 95), history).Should().BeNull(
            "9 samples is below the 10-sample floor the rule requires");
    }

    [Fact]
    public void SustainedHighCpu_fires_once_enough_samples_agree()
    {
        var rule    = new SustainedHighCpuRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 95, sampleCount: 20);

        var insight = rule.Evaluate(Healthy(cpu: 95), history);

        insight.Should().NotBeNull();
        insight!.Id.Should().Be("cpu.sustained-high");
    }

    [Fact]
    public void SustainedHighCpu_is_silent_when_the_average_is_moderate()
    {
        // A full window of samples averaging below the threshold is exactly the
        // case where a naive "current CPU is high" check would false-positive.
        var rule    = new SustainedHighCpuRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 40, sampleCount: 40);

        rule.Evaluate(Healthy(cpu: 99), history).Should().BeNull();
    }

    [Fact]
    public void SustainedHighCpu_confidence_rises_with_sample_count()
    {
        var rule = new SustainedHighCpuRule();

        var thin  = rule.Evaluate(Healthy(cpu: 95),
                        NoHistory().With(TelemetryMetric.Cpu, 95, sampleCount: 10))!;
        var full  = rule.Evaluate(Healthy(cpu: 95),
                        NoHistory().With(TelemetryMetric.Cpu, 95, sampleCount: 40))!;

        full.ConfidencePercent.Should().BeGreaterThan(thin.ConfidencePercent,
            "more evidence must mean more stated confidence");
        thin.ConfidencePercent.Should().BeInRange(1, 100);
        full.ConfidencePercent.Should().BeInRange(1, 100);
    }

    // ═══════════════ SystemRunningWellRule — the explicit all-clear ═══════════════

    [Fact]
    public void SystemRunningWell_is_silent_before_enough_history_exists()
    {
        var rule = new SystemRunningWellRule();

        rule.Evaluate(Healthy(), NoHistory()).Should().BeNull(
            "an all-clear asserted with no evidence would be as misleading as a false alarm");
    }

    [Fact]
    public void SystemRunningWell_fires_on_a_healthy_machine_with_history()
    {
        var rule    = new SystemRunningWellRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 15, sampleCount: 30);

        var insight = rule.Evaluate(Healthy(), history);

        insight.Should().NotBeNull();
        insight!.Id.Should().Be("system.healthy");
        insight.Severity.Should().Be(InsightSeverity.Info);
    }

    [Theory]
    [InlineData(85.0, 40.0, 50.0)]   // RAM under pressure
    [InlineData(40.0, 90.0, 50.0)]   // disk filling up
    [InlineData(40.0, 40.0, 95.0)]   // GPU pinned
    public void SystemRunningWell_withholds_the_all_clear_when_any_signal_is_bad(
        double ram, double disk, double gpu)
    {
        var rule    = new SystemRunningWellRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 15, sampleCount: 30);

        rule.Evaluate(Healthy(ram: ram, disk: disk, gpu: gpu), history)
            .Should().BeNull("a single bad signal must veto the all-clear");
    }

    [Fact]
    public void SystemRunningWell_withholds_the_all_clear_when_the_cpu_average_is_high()
    {
        var rule    = new SystemRunningWellRule();
        var history = NoHistory().With(TelemetryMetric.Cpu, average: 75, sampleCount: 30);

        rule.Evaluate(Healthy(), history).Should().BeNull();
    }

    // ═══════════════ Cross-cutting contracts ═══════════════

    /// <summary>Every rule registered on the engine, constructed with defaults.</summary>
    public static TheoryData<IInsightRule> AllRules() =>
    [
        new LowDiskSpaceRule(),
        new HighCpuTemperatureRule(),
        new SustainedHighCpuRule(),
        new SystemRunningWellRule(),
        new ElevatedRamPressureRule(),
        new HighDiskThroughputRule(),
        new GpuVramPressureRule(),
        new AbnormalGpuUsageRule(),
        new RisingRamTrendRule(),
        new WorseningThermalTrendRule(),
        new CpuEscalationRule(),
        new StartupItemCountRule(),
    ];

    /// <summary>
    /// The cross-cutting theories below only assert when a rule actually produces
    /// a finding. This guards them from becoming silently vacuous — if a refactor
    /// stopped every rule firing, those theories would still "pass" without this.
    /// </summary>
    [Fact]
    public void The_stressed_scenario_makes_a_meaningful_number_of_rules_fire()
    {
        var stressed = Healthy(cpu: 99, ram: 97, disk: 99, gpu: 99, tempC: 95);
        var history  = NoHistory()
            .With(TelemetryMetric.Cpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Ram,         average: 96, sampleCount: 40)
            .With(TelemetryMetric.Disk,        average: 95, sampleCount: 40)
            .With(TelemetryMetric.Gpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Temperature, average: 92, sampleCount: 40);

        var fired = AllRules()
            .Cast<object[]>()
            .Select(row => (IInsightRule)row[0])
            .Select(r => r.Evaluate(stressed, history))
            .Where(i => i is not null)
            .ToList();

        fired.Should().HaveCountGreaterThanOrEqualTo(4,
            "a machine at 99% CPU/disk/GPU, 97% RAM and 95 °C should trip several rules — " +
            "if it trips almost none, the cross-cutting assertions are testing nothing");

        fired.Select(i => i!.Id).Should().OnlyHaveUniqueItems(
            "two rules sharing an Id would silently deduplicate each other in the engine");
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void Every_rule_survives_a_cold_start_without_throwing(IInsightRule rule)
    {
        // The contract says rules return null on insufficient data. A rule that
        // throws here would take down the whole evaluation pass on first tick.
        var act = () => rule.Evaluate(Healthy(), NoHistory());

        act.Should().NotThrow();
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void Every_rule_exposes_a_stable_dotted_identifier(IInsightRule rule)
    {
        // The engine deduplicates on RuleId, so it must be stable and non-empty.
        rule.RuleId.Should().NotBeNullOrWhiteSpace();
        rule.RuleId.Should().MatchRegex("^[a-z0-9]+(\\.[a-z0-9-]+)+$",
            "rule ids are lowercase dot-separated, e.g. 'cpu.sustained-high'");
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void Every_rule_returns_the_same_id_it_advertises(IInsightRule rule)
    {
        // Drive each rule hard enough that most will produce a finding, then
        // assert the emitted Id matches RuleId — otherwise deduplication breaks.
        var stressed = Healthy(cpu: 99, ram: 97, disk: 99, gpu: 99, tempC: 95);
        var history  = NoHistory()
            .With(TelemetryMetric.Cpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Ram,         average: 96, sampleCount: 40)
            .With(TelemetryMetric.Disk,        average: 95, sampleCount: 40)
            .With(TelemetryMetric.Gpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Temperature, average: 92, sampleCount: 40);

        var insight = rule.Evaluate(stressed, history);

        if (insight is not null)
            insight.Id.Should().Be(rule.RuleId);
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void Every_finding_states_a_confidence_within_range(IInsightRule rule)
    {
        var stressed = Healthy(cpu: 99, ram: 97, disk: 99, gpu: 99, tempC: 95);
        var history  = NoHistory()
            .With(TelemetryMetric.Cpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Ram,         average: 96, sampleCount: 40)
            .With(TelemetryMetric.Disk,        average: 95, sampleCount: 40)
            .With(TelemetryMetric.Gpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Temperature, average: 92, sampleCount: 40);

        var insight = rule.Evaluate(stressed, history);

        if (insight is not null)
            insight.ConfidencePercent.Should().BeInRange(1, 100);
    }

    /// <summary>
    /// Guards the security-language doctrine in CLAUDE.md. Findings must be
    /// confidence-aware and descriptive; antivirus-style certainty language is
    /// what separates this product from a discount scanner, and it has drifted
    /// back in before. This test makes the drift fail the build.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRules))]
    public void No_finding_uses_alarmist_or_absolute_security_language(IInsightRule rule)
    {
        string[] banned =
        [
            "malicious", "infected", "dangerous", "threat detected",
            "virus", "malware", "critical error", "your pc is in danger",
        ];

        var stressed = Healthy(cpu: 99, ram: 97, disk: 99, gpu: 99, tempC: 95);
        var history  = NoHistory()
            .With(TelemetryMetric.Cpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Ram,         average: 96, sampleCount: 40)
            .With(TelemetryMetric.Disk,        average: 95, sampleCount: 40)
            .With(TelemetryMetric.Gpu,         average: 97, sampleCount: 40)
            .With(TelemetryMetric.Temperature, average: 92, sampleCount: 40);

        var insight = rule.Evaluate(stressed, history);
        if (insight is null) return;

        var prose = string.Join(' ',
            insight.Title, insight.Detail, insight.ActionHint ?? string.Empty).ToLowerInvariant();

        foreach (var term in banned)
            prose.Should().NotContain(term,
                $"rule '{rule.RuleId}' must use confidence-aware language, not '{term}'");
    }
}
