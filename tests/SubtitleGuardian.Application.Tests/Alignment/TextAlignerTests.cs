using SubtitleGuardian.Application.Alignment;
using SubtitleGuardian.Application.Text;
using SubtitleGuardian.Domain.Contracts;
using SubtitleGuardian.Domain.Text;
using Xunit;

namespace SubtitleGuardian.Application.Tests.Alignment;

public class TextAlignerTests
{
    private readonly TextAligner _aligner;
    private readonly SentenceSplitOptions _splitOptions;

    public TextAlignerTests()
    {
        _aligner = new TextAligner();
        _splitOptions = new SentenceSplitOptions { KeepPunctuation = true };
    }

    [Fact]
    public void Align_ShouldHandlePerfectMatch()
    {
        // Arrange
        var userText = "Hello world. This is a test.";
        var asrSegments = new List<Segment>
        {
            new Segment(0, 1000, "Hello world"),
            new Segment(1000, 2000, "This is a test")
        };

        // Act
        var result = _aligner.Align(asrSegments, userText, _splitOptions);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("Hello world.", result[0].Text);
        Assert.Equal(0, result[0].StartMs);
        Assert.Equal(1000, result[0].EndMs);
        
        Assert.Equal("This is a test.", result[1].Text);
        Assert.Equal(1000, result[1].StartMs);
        Assert.Equal(2000, result[1].EndMs);
    }

    [Fact]
    public void Align_ShouldHandleMissingWordsInAsr()
    {
        // Arrange
        var userText = "One two three four.";
        var asrSegments = new List<Segment>
        {
            new Segment(0, 1000, "One two four") // "three" is missing
        };

        // Act
        var result = _aligner.Align(asrSegments, userText, _splitOptions);

        // Assert
        Assert.Single(result);
        Assert.Equal("One two three four.", result[0].Text);
        Assert.Equal(0, result[0].StartMs);
        Assert.Equal(1000, result[0].EndMs);
    }

    [Fact]
    public void Align_ShouldHandleExtraWordsInAsr()
    {
        // Arrange
        var userText = "One two.";
        var asrSegments = new List<Segment>
        {
            new Segment(0, 1000, "One two three four") // "three four" extra
        };

        // Act
        var result = _aligner.Align(asrSegments, userText, _splitOptions);

        // Assert
        Assert.Single(result);
        Assert.Equal("One two.", result[0].Text);
    }

    [Fact]
    public void Align_ShouldInterpolateMissingTimestamps()
    {
        // Arrange
        var userText = "First sentence. Second sentence. Third sentence.";
        // ASR missed the middle sentence entirely
        var asrSegments = new List<Segment>
        {
            new Segment(0, 1000, "First sentence"),
            new Segment(2000, 3000, "Third sentence")
        };

        // Act
        var result = _aligner.Align(asrSegments, userText, _splitOptions);

        // Assert
        Assert.Equal(3, result.Count);
        
        // 1st
        Assert.Equal("First sentence.", result[0].Text);
        Assert.Equal(0, result[0].StartMs);
        Assert.Equal(1000, result[0].EndMs);

        // 2nd (Interpolated)
        Assert.Equal("Second sentence.", result[1].Text);
        Assert.True(result[1].StartMs >= 1000);
        Assert.True(result[1].EndMs <= 2000);
        Assert.True(result[1].EndMs > result[1].StartMs);

        // 3rd
        Assert.Equal("Third sentence.", result[2].Text);
        Assert.Equal(2000, result[2].StartMs);
        Assert.Equal(3000, result[2].EndMs);
    }

    [Fact]
    public void Align_ShouldHandleEmptyInputs()
    {
        Assert.Empty(_aligner.Align(new List<Segment>(), "Text"));
        Assert.Empty(_aligner.Align(new List<Segment> { new Segment(0, 100, "Hi") }, ""));
    }
}
