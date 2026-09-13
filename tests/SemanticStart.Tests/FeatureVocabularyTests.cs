using SemanticStart.Core.Model;
using SemanticStart.Core.Synthesis;
using Xunit;

namespace SemanticStart.Tests;

/// <summary>
/// The features column exists to say what the entity's other fields cannot. Both halves of that
/// are load-bearing and pull in opposite directions, so both are asserted here: a caption the name
/// already carries must go, and a caption nothing else carries must stay.
///
/// The first half is not tidiness. "edit a file" must not return Registry Editor, and the reason
/// it did was that "-editor" yields the verb "edit"; regedit's Edit menu then hands the ranker
/// back the match it had learned to refuse. The second half is the entire point of harvesting
/// interface labels at all - nothing written about Process Explorer mentions memory, while its
/// View menu offers "Physical Memory History".
///
/// Captions are kept whole rather than reduced to words. Adjacency is the only thing separating
/// "Virtual Memory" from a Virtual PC label next to a mention of memory, and the ranker now has
/// an arm that reads it - see RankingOptions.AdjacencyArmWeight - which it cannot do against a
/// field that has already been shredded.
/// </summary>
public sealed class FeatureVocabularyTests
{
    private static string? Features(string captions, string name, string summary) =>
        ProfileText.Features(
            [
                new EnrichmentDocument
                {
                    EntityId = "e",
                    Provider = "ui-resources",
                    IsOnline = false,
                    Text = "Interface labels: " + captions,
                },
            ],
            name,
            summary);

    [Fact]
    public void Features_DropsCaptionsTheNameAlreadyCarries()
    {
        var text = Features(
            "Edit, Registry Editor, Load Hive",
            "Registry Editor",
            "Open Registry Editor.");

        Assert.NotNull(text);
        Assert.DoesNotContain("Edit", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Load Hive", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Features_DropsCaptionsTheSummaryAlreadyCarries()
    {
        var text = Features(
            "System, Physical Memory History",
            "Process Explorer",
            "Freeware system monitor for Windows.");

        Assert.NotNull(text);
        Assert.DoesNotContain("System", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Physical Memory History", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Features_KeepsCapabilityWordsNothingElseStates()
    {
        var text = Features(
            "Physical Memory History, Own Memory Usage, Commit Charge",
            "Process Explorer",
            "Freeware system monitor for Windows.");

        Assert.NotNull(text);
        Assert.Contains("Memory", text, StringComparison.Ordinal);
        Assert.Contains("Usage", text, StringComparison.Ordinal);
        Assert.Contains("Commit", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The property the whole change turns on. A label is evidence about what the program does in
    /// the words the program uses for it, and splitting it into loose words destroys the only
    /// thing that distinguishes "Virtual Memory" from a Virtual PC label sitting beside a mention
    /// of memory.
    /// </summary>
    [Fact]
    public void Features_KeepsCaptionsWhole()
    {
        var text = Features(
            "Virtual Size, Physical Memory History, Virtual Memory",
            "Process Explorer",
            "Freeware system monitor for Windows.");

        Assert.NotNull(text);
        Assert.Contains("Virtual Memory", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A caption whose words have all been seen is still admitted when the pairing has not,
    /// because that pairing is the only place the program says those words belong together. This
    /// is exactly Process Explorer's case: "Virtual Size" spends virtual and "Physical Memory
    /// History" spends memory, and a rule that asked only for a new word would then refuse
    /// "Virtual Memory" - the one label that answers a search for it.
    /// </summary>
    [Fact]
    public void Features_AdmitsACaptionForANewPairingAlone()
    {
        var text = Features(
            "Virtual Size, Memory History, Virtual Memory",
            "Some Tool",
            "Does something.");

        Assert.NotNull(text);
        Assert.Contains("Virtual Memory", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The bound that survives from word-level de-duplication: a word already carried by the name
    /// cannot buy a caption a place, however many captions repeat it, or Registry Editor's four
    /// Edits read as four times the evidence for a query the corpus forbids it from answering.
    /// </summary>
    [Fact]
    public void Features_DoesNotReadmitRestatedWordsThroughPairings()
    {
        var text = Features(
            "Edit String, Edit Binary, Edit DWORD, Edit Multi",
            "String Editor",
            "Edit binary DWORD and multi values.");

        Assert.Null(text);
    }

    [Fact]
    public void Features_ShortWordsAreComparedByEqualityNotPrefix()
    {
        // "On" prefixes "Online", so a prefix rule without a floor would delete the second.
        var text = Features("Online Backup", "On", "Turns things on.");

        Assert.NotNull(text);
        Assert.Contains("Online", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Features_IsNullWhenEverythingRestatesTheName()
    {
        Assert.Null(Features("Registry Editor", "Registry Editor", "Open Registry Editor."));
    }

    /// <summary>
    /// The cap is what keeps one matching label meaningful. BM25 divides term frequency by field
    /// length, so an uncapped column lets a large program's whole interface bury the one label
    /// that answers the query. Process Explorer sat at rank 10 for "view memory usage" - outside
    /// the eight rows the overlay shows - on a "Physical Memory History" diluted across the rest.
    ///
    /// The bound asserted here is deliberately loose. The exact figure is a swept constant that
    /// moves whenever the corpus or the captions feeding it change, and pinning it exactly means
    /// this test fails for every re-tune while testing nothing the sweep does not already measure.
    /// What must not change is the property: a cap exists, and it is small enough that one label
    /// among a very large interface still registers.
    /// </summary>
    [Fact]
    public void Features_AreCappedSoOneMatchingLabelStillCounts()
    {
        var captions = string.Join(", ", Enumerable.Range(0, 2000).Select(i => $"Label{i}"));
        var text = Features(captions, "Thing", "Does things.");

        Assert.NotNull(text);

        var words = text!.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.StartsWith("Label", StringComparison.Ordinal))
            .ToArray();

        Assert.InRange(words.Length, 1, 1000);
        Assert.Equal(words.Length, words.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
