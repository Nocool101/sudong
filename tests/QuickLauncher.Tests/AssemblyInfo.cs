using Xunit;

// WPF 窗口/BAML 加载测试需要多个 STA 线程，System.IO.Packaging 的内部状态非线程安全，
// 并发加载会偶发竞态（PackagePart.CleanUpRequestedStreamsList）。禁用测试并行化。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
