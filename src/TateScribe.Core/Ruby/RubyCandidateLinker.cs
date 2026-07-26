using TateScribe.Core.Ocr;

namespace TateScribe.Core.Ruby;

public sealed record RubyCandidateLink(
    string BaseTextCandidate,
    double Confidence);

public static class RubyCandidateLinker
{
    public static RubyCandidateLink? Link(
        OcrWordReviewState ruby,
        IReadOnlyList<OcrWordReviewState> bodyWords)
    {
        var ranked = bodyWords
            .Where(body => body.Role == "Body"
                && body.AutomaticRole == "Body"
                && body.Word.Text.Length > 0)
            .Select(body => new
            {
                Body = body,
                Score = Score(ruby.Word, body.Word),
            })
            .Where(item => double.IsFinite(item.Score))
            .OrderBy(item => item.Score)
            .ThenBy(item => item.Body.Ordinal)
            .ToArray();
        if (ranked.Length == 0) return null;
        if (ranked.Length > 1 && Math.Abs(ranked[1].Score - ranked[0].Score) < 0.1)
            return null;
        return new RubyCandidateLink(
            ranked[0].Body.Word.Text,
            1d / (1d + ranked[0].Score));
    }

    private static double Score(OcrWord ruby, OcrWord body)
    {
        var rubyHeight = Math.Max(0, ruby.Bottom - ruby.Top);
        var bodyHeight = Math.Max(0, body.Bottom - body.Top);
        var overlap = Math.Min(ruby.Bottom, body.Bottom) - Math.Max(ruby.Top, body.Top);
        if (overlap <= 0 || rubyHeight <= 0 || bodyHeight <= 0)
            return double.PositiveInfinity;

        var horizontalGap = ruby.Left > body.Right
            ? ruby.Left - body.Right
            : body.Left > ruby.Right
                ? body.Left - ruby.Right
                : 0;
        var distanceScale = Math.Max(1, Math.Max(ruby.Right - ruby.Left, body.Right - body.Left));
        if (horizontalGap > distanceScale * 3)
            return double.PositiveInfinity;
        var overlapRatio = overlap / Math.Min(rubyHeight, bodyHeight);
        var rubyPerCharacter = rubyHeight / Math.Max(1, ruby.Text.Length);
        var bodyPerCharacter = bodyHeight / Math.Max(1, body.Text.Length);
        var heightDifference = Math.Abs(rubyPerCharacter - bodyPerCharacter)
            / Math.Max(1, Math.Max(rubyPerCharacter, bodyPerCharacter));
        return horizontalGap / distanceScale
            + (1 - Math.Min(1, overlapRatio)) * 0.25
            + heightDifference * 0.25;
    }
}
