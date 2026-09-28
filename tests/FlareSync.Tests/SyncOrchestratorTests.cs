using System.Net;
using FlareSync.Core.Abstractions;
using FlareSync.Core.Models;
using FlareSync.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlareSync.Tests;

public class SyncOrchestratorTests
{
    private static readonly IPAddress V4 = IPAddress.Parse("203.0.114.1");
    private static readonly IPAddress V6 = IPAddress.Parse("2a03:1b20:bef1::1");

    private sealed class FakeResolver(IPAddress? v4, IPAddress? v6) : IIpResolver
    {
        public List<IpFamily> Calls { get; } = [];

        public Task<IpDetection> DetectAsync(IpFamily family, CancellationToken cancellationToken)
        {
            Calls.Add(family);
            var address = family == IpFamily.IPv4 ? v4 : v6;
            return Task.FromResult(new IpDetection(family, address, address is null ? null : "fake", address is null ? ["down"] : []));
        }
    }

    private sealed class FakeProvider(params DnsTarget[] targets) : IDnsProvider
    {
        public string Name => "fake";

        /// <summary>One entry per changed family, flattened from <see cref="Updates"/>.</summary>
        public List<(string Hostname, IpFamily Family, IPAddress Address)> Upserts { get; } = [];

        public List<DnsUpdate> Updates { get; } = [];

        public HashSet<string> Failing { get; } = [];

        public Task<IReadOnlyList<DnsTarget>> GetTargetsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DnsTarget>>(targets);

        public Task<IReadOnlyDictionary<IpFamily, SyncResult>> UpdateAsync(DnsUpdate update, CancellationToken cancellationToken)
        {
            Updates.Add(update);
            foreach (var family in update.Changed)
            {
                Upserts.Add((update.Target.Hostname, family, update.Addresses[family]));
            }

            if (Failing.Contains(update.Target.Hostname))
            {
                throw new InvalidOperationException("api down");
            }

            return Task.FromResult<IReadOnlyDictionary<IpFamily, SyncResult>>(update.Changed.ToDictionary(f => f, _ => SyncResult.Updated()));
        }
    }

    private sealed class ListLogger : ILogger<SyncOrchestrator>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class SwitchingResolver : IIpResolver
    {
        public IPAddress? V6 { get; set; }

        public Task<IpDetection> DetectAsync(IpFamily family, CancellationToken cancellationToken)
        {
            var address = family == IpFamily.IPv4 ? V4 : V6;
            return Task.FromResult(new IpDetection(family, address, "fake", address is null ? ["down"] : []));
        }
    }

    private static SyncOrchestrator Create(TempConfig temp, FakeProvider provider, IIpResolver resolver, ILogger<SyncOrchestrator>? logger = null)
        => new([provider], resolver, new SyncStateStore(temp.Paths, temp.Store, TimeProvider.System), logger ?? NullLogger<SyncOrchestrator>.Instance);

    [Fact]
    [Trait("Req", "FR-016")]
    public async Task Reports_host_and_new_address_on_success_and_failure()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(
            new DnsTarget("fake", "good.example.com", true, false),
            new DnsTarget("fake", "bad.example.com", true, false));
        provider.Failing.Add("bad.example.com");
        var logger = new ListLogger();

        await Create(temp, provider, new FakeResolver(V4, null), logger).RunOnceAsync(force: false, CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information
            && e.Message.Contains("good.example.com") && e.Message.Contains(V4.ToString()));
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error
            && e.Message.Contains("bad.example.com") && e.Message.Contains("api down"));
    }

    [Fact]
    [Trait("Req", "FR-016")]
    public async Task Detection_failure_is_warned_once_until_it_recovers()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, true));
        var resolver = new SwitchingResolver();
        var logger = new ListLogger();
        var orchestrator = Create(temp, provider, resolver, logger);

        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);
        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);
        resolver.V6 = V6;
        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);

        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("detected again"));
    }

    [Fact]
    [Trait("Req", "FR-305")]
    public async Task Sends_one_update_per_target_with_every_detected_address()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, true));

        await Create(temp, provider, new FakeResolver(V4, V6)).RunOnceAsync(force: false, CancellationToken.None);

        var update = Assert.Single(provider.Updates);
        Assert.Equal(V4, update.Addresses[IpFamily.IPv4]);
        Assert.Equal(V6, update.Addresses[IpFamily.IPv6]);
        Assert.Equal([IpFamily.IPv4, IpFamily.IPv6], update.Changed.Order());
    }

    [Fact]
    [Trait("Req", "FR-305")]
    public async Task Unchanged_family_is_sent_along_but_not_marked_changed()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, true));
        var resolver = new SwitchingResolver { V6 = null };
        var orchestrator = Create(temp, provider, resolver);

        await orchestrator.RunOnceAsync(force: false, CancellationToken.None); // IPv4 applied, IPv6 missing
        resolver.V6 = V6;
        var report = await orchestrator.RunOnceAsync(force: false, CancellationToken.None);

        var update = provider.Updates[^1];
        Assert.Equal(2, update.Addresses.Count);
        Assert.Equal([IpFamily.IPv6], update.Changed);
        Assert.Equal(SyncOutcome.Unchanged, report.Entries.Single(e => e.Family == IpFamily.IPv4).Result.Outcome);
        Assert.Equal(SyncOutcome.Updated, report.Entries.Single(e => e.Family == IpFamily.IPv6).Result.Outcome);
    }

    [Fact]
    [Trait("Req", "FR-011")]
    public async Task Skips_records_whose_address_did_not_change()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, true));
        var orchestrator = Create(temp, provider, new FakeResolver(V4, V6));

        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);
        var second = await orchestrator.RunOnceAsync(force: false, CancellationToken.None);

        Assert.Equal(2, provider.Upserts.Count);
        Assert.All(second.Entries, e => Assert.Equal(SyncOutcome.Unchanged, e.Result.Outcome));
    }

    [Fact]
    [Trait("Req", "FR-014")]
    public async Task Force_updates_every_record()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, false));
        var orchestrator = Create(temp, provider, new FakeResolver(V4, V6));

        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);
        await orchestrator.RunOnceAsync(force: true, CancellationToken.None);

        Assert.Equal(2, provider.Upserts.Count);
    }

    [Fact]
    [Trait("Req", "FR-012")]
    public async Task Failure_in_one_record_does_not_block_others()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(
            new DnsTarget("fake", "bad.example.com", true, false),
            new DnsTarget("fake", "good.example.com", true, false));
        provider.Failing.Add("bad.example.com");
        var orchestrator = Create(temp, provider, new FakeResolver(V4, null));

        var report = await orchestrator.RunOnceAsync(force: false, CancellationToken.None);

        Assert.True(report.HasFailures);
        Assert.Equal(SyncOutcome.Updated, report.Entries.Single(e => e.Hostname == "good.example.com").Result.Outcome);

        // The failed record is retried next cycle; the good one is not.
        provider.Failing.Clear();
        await orchestrator.RunOnceAsync(force: false, CancellationToken.None);
        Assert.Equal(3, provider.Upserts.Count);
        Assert.Equal("bad.example.com", provider.Upserts[^1].Hostname);
    }

    [Fact]
    [Trait("Req", "FR-013")]
    public async Task Missing_family_is_skipped()
    {
        using var temp = new TempConfig();
        var provider = new FakeProvider(new DnsTarget("fake", "a.example.com", true, true));
        var orchestrator = Create(temp, provider, new FakeResolver(V4, null));

        var report = await orchestrator.RunOnceAsync(force: false, CancellationToken.None);

        Assert.Equal(SyncOutcome.Skipped, report.Entries.Single(e => e.Family == IpFamily.IPv6).Result.Outcome);
        Assert.Single(provider.Upserts);
    }

    [Fact]
    [Trait("Req", "FR-010")]
    public async Task Only_needed_families_are_detected()
    {
        using var temp = new TempConfig();
        var resolver = new FakeResolver(V4, V6);
        var provider = new FakeProvider(
            new DnsTarget("fake", "a.example.com", true, false),
            new DnsTarget("fake", "b.example.com", true, false));

        await Create(temp, provider, resolver).RunOnceAsync(force: false, CancellationToken.None);

        Assert.Equal([IpFamily.IPv4], resolver.Calls);
    }
}
