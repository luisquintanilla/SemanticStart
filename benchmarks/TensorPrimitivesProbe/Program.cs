using System.Diagnostics;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

const int dimensions = 384;
const int rows = 10_000;
const int dotRepetitions = 10_000;
const int warmupIterations = 5;
const int measuredIterations = 20;

var random = new Random(1977);
var query = CreateVector(dimensions, random);
var matrix = new float[rows * dimensions];
for (var i = 0; i < matrix.Length; i++)
    matrix[i] = random.NextSingle() * 2f - 1f;

var legacyDot = LegacyDot(query, matrix.AsSpan(0, dimensions));
var tensorDot = TensorPrimitives.Dot(query, matrix.AsSpan(0, dimensions));
var dotDifference = Math.Abs(legacyDot - tensorDot);
var legacyMatrix = LegacyMatrixScan(query, matrix, dimensions);
var tensorMatrix = TensorMatrixScan(query, matrix, dimensions);
var matrixDifference = Math.Abs(legacyMatrix - tensorMatrix);

Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
Console.WriteLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"Vector<float>.Count: {Vector<float>.Count}");
Console.WriteLine($"Vector hardware acceleration: {Vector.IsHardwareAccelerated}");
Console.WriteLine($"Dimensions: {dimensions:N0}");
Console.WriteLine($"Rows: {rows:N0}");
Console.WriteLine($"Repeated dots per single-dot measurement: {dotRepetitions:N0}");
Console.WriteLine($"Warmup iterations: {warmupIterations:N0}");
Console.WriteLine($"Measured iterations: {measuredIterations:N0}");
Console.WriteLine();
Console.WriteLine("Method                       Dot products/sec   Mean (ms)   Checksum");

var legacyDotResult = Measure(
    "Legacy Vector<float> dot",
    warmupIterations,
    measuredIterations,
    dotRepetitions,
    () => RepeatLegacyDot(dotRepetitions, query, matrix.AsSpan(0, dimensions)));
var tensorDotResult = Measure(
    "TensorPrimitives dot",
    warmupIterations,
    measuredIterations,
    dotRepetitions,
    () => RepeatTensorDot(dotRepetitions, query, matrix.AsSpan(0, dimensions)));
var legacyMatrixResult = Measure(
    "Legacy Vector<float> matrix scan",
    warmupIterations,
    measuredIterations,
    rows,
    () => LegacyMatrixScan(query, matrix, dimensions));
var tensorMatrixResult = Measure(
    "TensorPrimitives matrix scan",
    warmupIterations,
    measuredIterations,
    rows,
    () => TensorMatrixScan(query, matrix, dimensions));

Console.WriteLine();
Console.WriteLine($"Single-dot throughput ratio: {tensorDotResult.DotProductsPerSecond / legacyDotResult.DotProductsPerSecond:F2}x");
Console.WriteLine($"Matrix-scan throughput ratio: {tensorMatrixResult.DotProductsPerSecond / legacyMatrixResult.DotProductsPerSecond:F2}x");
Console.WriteLine($"Single-dot absolute difference: {dotDifference:E3}");
Console.WriteLine($"Matrix-scan absolute difference: {matrixDifference:E3}");

if (dotDifference > 1e-4f || matrixDifference > 1e-2f)
    throw new InvalidOperationException("The benchmark implementations produced materially different results.");

static float[] CreateVector(int dimensions, Random random)
{
    var vector = new float[dimensions];
    for (var i = 0; i < vector.Length; i++)
        vector[i] = random.NextSingle() * 2f - 1f;

    return vector;
}

static float LegacyDot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
{
    var sum = 0f;
    var i = 0;

    if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
    {
        var accumulator = Vector<float>.Zero;
        var limit = a.Length - (a.Length % Vector<float>.Count);
        for (; i < limit; i += Vector<float>.Count)
        {
            accumulator += new Vector<float>(a.Slice(i, Vector<float>.Count))
                * new Vector<float>(b.Slice(i, Vector<float>.Count));
        }

        sum = Vector.Dot(accumulator, Vector<float>.One);
    }

    for (; i < a.Length; i++)
        sum += a[i] * b[i];

    return sum;
}

static float LegacyMatrixScan(float[] query, float[] matrix, int dimensions)
{
    var checksum = 0f;
    for (var row = 0; row < matrix.Length / dimensions; row++)
        checksum += LegacyDot(query, matrix.AsSpan(row * dimensions, dimensions));

    return checksum;
}

static float RepeatLegacyDot(int repetitions, float[] query, ReadOnlySpan<float> row)
{
    var checksum = 0f;
    for (var i = 0; i < repetitions; i++)
        checksum += LegacyDot(query, row);

    return checksum;
}

static float RepeatTensorDot(int repetitions, float[] query, ReadOnlySpan<float> row)
{
    var checksum = 0f;
    for (var i = 0; i < repetitions; i++)
        checksum += TensorPrimitives.Dot(query, row);

    return checksum;
}

static float TensorMatrixScan(float[] query, float[] matrix, int dimensions)
{
    var checksum = 0f;
    for (var row = 0; row < matrix.Length / dimensions; row++)
        checksum += TensorPrimitives.Dot(query, matrix.AsSpan(row * dimensions, dimensions));

    return checksum;
}

static Measurement Measure(
    string name,
    int warmupIterations,
    int measuredIterations,
    int dotProductsPerIteration,
    Func<float> operation)
{
    for (var i = 0; i < warmupIterations; i++)
        _ = operation();

    var stopwatch = Stopwatch.StartNew();
    var checksum = 0f;
    for (var i = 0; i < measuredIterations; i++)
        checksum += operation();

    stopwatch.Stop();
    var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
    var dotProducts = (double)measuredIterations * dotProductsPerIteration;
    var dotProductsPerSecond = dotProducts / elapsedSeconds;

    Console.WriteLine(
        $"{name,-30} {dotProductsPerSecond,17:N0} {stopwatch.Elapsed.TotalMilliseconds / measuredIterations,11:F3} {checksum,12:F3}");

    return new Measurement(dotProductsPerSecond);
}

readonly record struct Measurement(double DotProductsPerSecond);
