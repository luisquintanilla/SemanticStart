# Benchmarks

These probes are intentionally separate from the production solution. They provide a small,
dependency-light way to reproduce performance claims without adding benchmark runtime overhead to
the application or test projects.

## TensorPrimitives vs. handwritten SIMD

Run from the repository root in a Release build. Disable tiered compilation so the comparison
measures steady-state JIT code consistently across runs:

```powershell
$env:COMPlus_TieredCompilation = '0'
dotnet run --configuration Release --project .\benchmarks\TensorPrimitivesProbe\TensorPrimitivesProbe.csproj
```

The probe compares the previous handwritten `Vector<float>` implementation with
`TensorPrimitives.Dot` for:

- one 384-dimensional dot product;
- a 10,000-row scan of 384-dimensional vectors.

It uses the same deterministic input data for both implementations. The single-dot case repeats
the dot 10,000 times per measurement so stopwatch resolution does not dominate; the matrix case
scans all 10,000 rows per measurement. Both perform five warmup iterations, then measure twenty
iterations with `Stopwatch`. The probe prints runtime, architecture, SIMD width, dot-products per
second, mean iteration time, checksums, speedup ratios, and absolute numerical differences. A
materially different result fails the process so an accidental benchmark change cannot quietly
produce a performance-only result.

Representative output from the PR validation machine:

| Method | Dot products/sec | Mean per iteration | Checksum |
|---|---:|---:|---:|
| Legacy `Vector<float>` dot | 17,698,332 | 0.565 ms | -739,387.125 |
| `TensorPrimitives` dot | 37,618,027 | 0.266 ms | -739,387.125 |
| Legacy `Vector<float>` matrix scan | 7,151,106 | 1.398 ms | -11,987.123 |
| `TensorPrimitives` matrix scan | 10,276,014 | 0.973 ms | -11,987.104 |

| Comparison | Result |
|---|---:|
| Single-dot throughput ratio | **2.13x** |
| Matrix-scan throughput ratio | **1.44x** |
| Single-dot absolute difference | `7.153e-7` |
| Matrix-scan absolute difference | `9.155e-4` |

The run used .NET 10.0.12 on Windows x64 with 384 dimensions, 10,000 rows, 10,000 repeated
single dots per measurement, five warmup iterations, and twenty measured iterations.

The exact timings vary with CPU load, runtime patch, and processor. Compare the throughput ratio
and absolute difference on the same machine rather than treating one raw throughput number as a
portable guarantee. The matrix scan is the relevant retrieval-shaped measurement; the single-dot
case isolates the primitive itself.
