using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace ZeroPinyin.Benchmarks;

public class FileInfoColumn(string columnName, Func<TestFileParam, string> selector) : IColumn {

	public string Id => ColumnName;
	public string ColumnName { get; } = columnName;
	public bool AlwaysShow => true;
	public ColumnCategory Category => ColumnCategory.Params;
	public int PriorityInCategory => 1;
	public bool IsNumeric => true;
	public UnitType UnitType => UnitType.Dimensionless;
	public string Legend => ColumnName;

	public string GetValue(Summary summary, BenchmarkCase benchmarkCase) {
		return benchmarkCase.Parameters.Items.FirstOrDefault(x => x.Name == "FileParam")?.Value is TestFileParam param
			? selector(param)
			: "-";
	}

	public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) =>
		GetValue(summary, benchmarkCase);

	public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
	public bool IsAvailable(Summary summary) => true;
}