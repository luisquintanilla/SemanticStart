using SemanticStart.Core.Collectors;
using SemanticStart.Core.Model;

namespace SemanticStart.Tests;

public sealed class PowerToysCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ss-powertoys-" + Guid.NewGuid().ToString("N"));

    public PowerToysCollectorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Collect_ExposesUtilitiesWithSettingsDeepLinksAndOfficialDocumentation()
    {
        var runner = CreateRunner();
        var entities = await CollectAsync(runner);

        var colorPicker = Assert.Single(entities, entity => entity.DisplayName == "Color Picker");
        Assert.Equal("powertoys", colorPicker.Source);
        Assert.Equal(LaunchKind.Executable, colorPicker.LaunchKind);
        Assert.Equal(runner, colorPicker.LaunchTarget);
        Assert.Equal("--open-settings=ColorPicker", colorPicker.LaunchArguments);
        Assert.Equal("https://learn.microsoft.com/en-us/windows/powertoys/color-picker", colorPicker.RawMetadata["learnArticle"]);
        Assert.Contains("color", colorPicker.RawMetadata["description"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Collect_ScopesSameNamedStandaloneProductsSeparately()
    {
        var entities = await CollectAsync(CreateRunner());

        var zoomIt = Assert.Single(entities, entity => entity.DisplayName == "ZoomIt");
        var commandPalette = Assert.Single(entities, entity => entity.DisplayName == "Command Palette");
        Assert.Equal("Microsoft PowerToys", zoomIt.RawMetadata["dedupeScope"]);
        Assert.False(commandPalette.RawMetadata.ContainsKey("dedupeScope"));
    }

    [Fact]
    public async Task Collect_ProducesStableIdsAndHashes()
    {
        var runner = CreateRunner();

        var first = await CollectAsync(runner);
        var second = await CollectAsync(runner);

        Assert.Equal(first.Select(entity => entity.Id), second.Select(entity => entity.Id));
        Assert.Equal(first.Select(entity => entity.ContentHash), second.Select(entity => entity.ContentHash));
        Assert.Equal(first.Count, first.Select(entity => entity.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(first, entity => Assert.StartsWith("powertoys:", entity.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Collect_ReportsNothingWhenPowerToysIsNotInstalled()
    {
        var collector = new PowerToysCollector(() => Path.Combine(_root, "missing", "PowerToys.exe"));

        Assert.False(collector.IsSupported);
        Assert.Empty(await CollectAsync(collector));
    }

    private string CreateRunner()
    {
        var runner = Path.Combine(_root, "PowerToys.exe");
        File.WriteAllText(runner, "test");
        return runner;
    }

    private static async Task<List<Entity>> CollectAsync(string runner) =>
        await CollectAsync(new PowerToysCollector(() => runner));

    private static async Task<List<Entity>> CollectAsync(PowerToysCollector collector)
    {
        var entities = new List<Entity>();
        await foreach (var entity in collector.CollectAsync())
            entities.Add(entity);
        return entities;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
