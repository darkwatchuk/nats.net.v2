using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using NATS.Client.Core;
using NATS.Client.Core.Internal;

namespace MicroBenchmark;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
public class NewInboxBenchmarks
{
    private static readonly NatsOpts LongPrefixOpt = NatsOpts.Default
        with
    {
        InboxPrefix = "this-is-a-rather-long-prefix-that-we-use-here",
    };

    private static readonly NatsConnection ConnectionDefaultPrefix = new();
    private static readonly NatsConnection ConnectionLongPrefix = new(LongPrefixOpt);

    private char[] _buf = new char[32];

    [GlobalSetup]
    public void Setup()
    {
        Nuid.TryWriteNuid(new char[100]);
    }

    [Benchmark(Baseline = true)]
    [SkipLocalsInit]
    public bool TryWriteNuid()
    {
        return Nuid.TryWriteNuid(_buf);
    }

    [Benchmark]
    public string NewInbox_ShortPrefix()
    {
        return ConnectionDefaultPrefix.NewInbox();
    }

    [Benchmark]
    public string NewInbox_LongPrefix()
    {
        return ConnectionLongPrefix.NewInbox();
    }
}
