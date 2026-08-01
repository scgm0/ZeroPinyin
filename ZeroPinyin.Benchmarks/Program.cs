using BenchmarkDotNet.Running;
using ZeroPinyin.Benchmarks;

if (args.Length > 0 && args[0] == "--quick") {
	QuickBenchmark.Run();
	return;
}

BenchmarkRunner.Run<ZeroPinyinBenchmarks>();
