using BenchmarkDotNet.Configs;

namespace ZeroPinyin.Benchmarks;

public class CustomBenchmarkConfig : ManualConfig {
	public CustomBenchmarkConfig() {
		Add(DefaultConfig.Instance);
		AddColumn(new FileInfoColumn("Size", p => p.SizeStr));
		AddColumn(new FileInfoColumn("Lines", p => p.Lines.Length.ToString("N0")));
	}
}