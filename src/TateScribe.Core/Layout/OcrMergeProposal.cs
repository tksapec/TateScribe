namespace TateScribe.Core.Layout;

public enum OcrMergeOperationType
{
    Insertion,
    Replacement,
    Deletion
}

public sealed record OcrMergeOperation(
    OcrMergeOperationType Type,
    int SuggestedTextIndex,
    string OriginalText,
    string ProposedText,
    int? AnchorWordOrdinal,
    double Confidence,
    string Reason);

public sealed record OcrMergeProposal(
    string SuggestedText,
    IReadOnlyList<OcrMergeOperation> Operations,
    IReadOnlyList<ReviewItem> ReviewItems);
