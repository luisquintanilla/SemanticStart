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

### Interpretation

- **The isolated primitive is substantially faster.** `TensorPrimitives.Dot` processes about
  2.13 times as many 384-dimensional dot products per second. Its measured iteration time falls
  from 0.565 ms to 0.266 ms, a reduction of about 53%.
- **The retrieval-shaped scan is still meaningfully faster.** Scanning 10,000 stored vectors
  improves from 7.15 million to 10.28 million dot products per second, or about 1.44 times the
  baseline throughput. The measured scan time falls from 1.398 ms to 0.973 ms, about 30% lower.
- **The math remains equivalent for this workload.** The isolated result differs by only
  `7.153e-7`. The matrix checksum differs by `9.155e-4` over a checksum of roughly `-11,987`,
  which is about `7.6e-8` relative error. These differences are expected when SIMD
  implementations accumulate floating-point values in different orders.
- **This validates the vector-math replacement, not model quality.** The probe measures dot
  products used by retrieval; it does not compare ONNX model outputs, ranking quality, or
  end-to-end query latency. Those remain covered by the existing application tests and should be
  measured separately if needed.

The result supports keeping TensorPrimitives in the query hot path: it simplifies the code and
improves the measured scan throughput without a material numerical change.
