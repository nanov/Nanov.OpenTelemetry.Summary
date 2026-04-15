using BenchmarkDotNet.Running;
using Nanov.OpenTelemetry.Summary.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(RecordBenchmarks).Assembly).Run(args);
