using System.Net;
using SemanticStart.Core.Enrichment;
using SemanticStart.Core.Model;

namespace SemanticStart.Tests;

public sealed class LearnEnricherTests
{
    [Fact]
    public async Task Enrich_UsesCollectorSuppliedOfficialLearnArticleDirectly()
    {
        var slug = "semanticstart-test-" + Guid.NewGuid().ToString("N");
        var article = $"https://learn.microsoft.com/en-us/windows/powertoys/{slug}";
        var handler = new RecordingHandler("""
            <html>
              <head>
                <meta name="description" content="Pick a color from any visible pixel and copy its value." />
              </head>
            </html>
            """);
        using var http = new HttpClient(handler);
        var enricher = new LearnEnricher(http);
        var entity = new Entity
        {
            Id = "powertoys:colorpicker",
            Kind = EntityKind.Application,
            DisplayName = "Color Picker",
            LaunchKind = LaunchKind.Executable,
            LaunchTarget = @"C:\PowerToys\PowerToys.exe",
            Source = "powertoys",
            RawMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["learnArticle"] = article,
            },
        };

        var document = Assert.Single(await enricher.EnrichAsync(entity));

        Assert.Equal(article, document.SourceUri);
        Assert.Contains("Pick a color", document.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([article], handler.Requests);
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.GetLeftPart(UriPartial.Path));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response),
            });
        }
    }
}
