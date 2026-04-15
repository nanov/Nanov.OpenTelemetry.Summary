// * Summary *

BenchmarkDotNet v0.14.0, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]   : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD
.NET 9.0 : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD

Job=.NET 9.0  Runtime=.NET 9.0

| Method                          | SampleCount | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|-------------------------------- |------------ |------------:|----------:|----------:|------:|--------:|---------:|-------:|----------:|------------:|
| OtelReservoir_RecordAndSnapshot | 100         |    10.34 us |  0.061 us |  0.057 us |  1.00 |    0.01 |        - |      - |         - |          NA |
| AppMetrics_RecordAndSnapshot    | 100         |    16.08 us |  0.151 us |  0.133 us |  1.56 |    0.01 |   1.7700 |      - |   14952 B |          NA |
|                                 |             |             |           |           |       |         |          |        |           |             |
| OtelReservoir_RecordAndSnapshot | 1000        |   179.40 us |  1.146 us |  1.016 us |  1.00 |    0.01 |        - |      - |         - |          NA |
| AppMetrics_RecordAndSnapshot    | 1000        |   261.17 us |  1.487 us |  1.319 us |  1.46 |    0.01 |  17.0898 | 0.4883 |  144552 B |          NA |
|                                 |             |             |           |           |       |         |          |        |           |             |
| OtelReservoir_RecordAndSnapshot | 10000       |   885.39 us |  2.668 us |  2.228 us |  1.00 |    0.00 |        - |      - |         - |          NA |
| AppMetrics_RecordAndSnapshot    | 10000       | 1,671.68 us | 17.219 us | 14.379 us |  1.89 |    0.02 | 119.1406 |      - | 1009896 B |          NA |

// * Hints *
Outliers
SnapshotBenchmarks.AppMetrics_RecordAndSnapshot: .NET 9.0    -> 1 outlier  was  removed (17.79 us)
SnapshotBenchmarks.OtelReservoir_RecordAndSnapshot: .NET 9.0 -> 1 outlier  was  removed (187.06 us)
SnapshotBenchmarks.AppMetrics_RecordAndSnapshot: .NET 9.0    -> 1 outlier  was  removed (264.95 us)
SnapshotBenchmarks.OtelReservoir_RecordAndSnapshot: .NET 9.0 -> 2 outliers were removed, 3 outliers were detected (879.68 us, 894.19 us, 894.55 us)
SnapshotBenchmarks.AppMetrics_RecordAndSnapshot: .NET 9.0    -> 2 outliers were removed (1.74 ms, 1.79 ms)

// * Summary *

BenchmarkDotNet v0.14.0, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]   : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD
.NET 9.0 : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD

Job=.NET 9.0  Runtime=.NET 9.0

| Method                                | Mean       | Error     | StdDev    | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------------- |-----------:|----------:|----------:|-------:|--------:|-------:|----------:|------------:|
| Summary_Record_NoTags                 |   1.816 ns | 0.0228 ns | 0.0213 ns |   1.00 |    0.02 |      - |         - |          NA |
| Summary_Record_WithTags               |   4.348 ns | 0.0274 ns | 0.0256 ns |   2.39 |    0.03 |      - |         - |          NA |
| AppMetrics_Record_NoTags              | 267.094 ns | 3.2692 ns | 3.0580 ns | 147.06 |    2.33 | 0.1163 |     976 B |          NA |
| AppMetrics_Record_WithTags            | 237.320 ns | 2.2810 ns | 2.1337 ns | 130.66 |    1.86 | 0.1023 |     856 B |          NA |
| PrometheusNet_Record_NoLabels         | 116.969 ns | 2.1259 ns | 1.8846 ns |  64.40 |    1.24 |      - |         - |          NA |
| PrometheusNet_Record_CachedChild      | 116.451 ns | 1.0621 ns | 0.9415 ns |  64.12 |    0.88 |      - |         - |          NA |
| PrometheusNet_Record_WithLabelsLookup | 179.891 ns | 1.4195 ns | 1.3278 ns |  99.05 |    1.33 | 0.0057 |      48 B |          NA |

// * Hints *
Outliers
RecordBenchmarks.PrometheusNet_Record_NoLabels: .NET 9.0    -> 1 outlier  was  removed (135.51 ns)
RecordBenchmarks.PrometheusNet_Record_CachedChild: .NET 9.0 -> 1 outlier  was  removed (125.34 ns)

// * Summary *

BenchmarkDotNet v0.14.0, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M4 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 9.0.307
[Host]   : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD
.NET 9.0 : .NET 9.0.11 (9.0.1125.51716), Arm64 RyuJIT AdvSIMD

Job=.NET 9.0  Runtime=.NET 9.0

| Method                         | ThreadCount | Mean        | Error     | StdDev      | Median      | Ratio | RatioSD | Gen0      | Allocated  | Alloc Ratio |
|------------------------------- |------------ |------------:|----------:|------------:|------------:|------:|--------:|----------:|-----------:|------------:|
| Summary_ConcurrentRecord       | 4           |    621.4 us |  12.26 us |    21.48 us |    620.7 us |  1.00 |    0.05 |         - |      721 B |        1.00 |
| AppMetrics_ConcurrentRecord    | 4           |  6,766.0 us | 628.87 us | 1,854.23 us |  5,661.7 us | 10.90 |    3.00 | 4671.8750 | 39040721 B |   54,148.02 |
| PrometheusNet_ConcurrentRecord | 4           | 15,367.2 us | 172.84 us |   161.68 us | 15,304.3 us | 24.76 |    0.87 |         - |      736 B |        1.02 |
|                                |             |             |           |             |             |       |         |           |            |             |
| Summary_ConcurrentRecord       | 8           |  2,323.9 us |  43.31 us |    95.06 us |  2,303.4 us |  1.00 |    0.06 |         - |     1296 B |        1.00 |
| AppMetrics_ConcurrentRecord    | 8           | 17,006.1 us | 234.11 us |   207.53 us | 16,963.7 us |  7.33 |    0.30 | 9375.0000 | 78081328 B |   60,247.94 |
| PrometheusNet_ConcurrentRecord | 8           | 31,578.8 us | 146.72 us |   137.24 us | 31,558.4 us | 13.61 |    0.54 |         - |     1360 B |        1.05 |

// * Warnings *
MultimodalDistribution
ConcurrentRecordBenchmarks.AppMetrics_ConcurrentRecord: .NET 9.0 -> It seems that the distribution can have several modes (mValue = 3.15)

// * Hints *
Outliers
ConcurrentRecordBenchmarks.Summary_ConcurrentRecord: .NET 9.0    -> 3 outliers were removed (689.12 us..957.58 us)
ConcurrentRecordBenchmarks.Summary_ConcurrentRecord: .NET 9.0    -> 3 outliers were removed (2.65 ms..2.89 ms)
ConcurrentRecordBenchmarks.AppMetrics_ConcurrentRecord: .NET 9.0 -> 1 outlier  was  removed (17.60 ms)
