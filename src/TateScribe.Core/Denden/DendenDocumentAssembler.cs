using TateScribe.Core.Ruby;

namespace TateScribe.Core.Denden;

public static class DendenDocumentAssembler
{
    public static DendenExportDocument Assemble(
        StructuredDocument document,
        IReadOnlyDictionary<Guid, int> pageSortOrders,
        IReadOnlyList<DendenIllustration> illustrations)
    {
        var pending = new Queue<DendenIllustration>(
            illustrations.OrderBy(item => item.SortOrder).ThenBy(item => item.PageId));
        var blocks = new List<DendenContentBlock>();
        foreach (var paragraph in document.Paragraphs)
        {
            var sourceOrders = paragraph.SourceSpans
                .Select(span => pageSortOrders.GetValueOrDefault(span.PageId, int.MaxValue))
                .Where(order => order != int.MaxValue)
                .Distinct()
                .Order()
                .ToArray();
            if (sourceOrders.Length == 0)
            {
                blocks.Add(new DendenParagraphBlock(paragraph));
                continue;
            }

            var firstOrder = sourceOrders[0];
            var lastOrder = sourceOrders[^1];
            while (pending.TryPeek(out var before) && before.SortOrder < firstOrder)
                blocks.Add(new DendenIllustrationBlock(pending.Dequeue()));

            blocks.Add(new DendenParagraphBlock(paragraph));
            while (pending.TryPeek(out var inside) && inside.SortOrder < lastOrder)
            {
                var illustration = pending.Dequeue();
                blocks.Add(new DendenIllustrationBlock(
                    illustration,
                    PlacementAdjusted: illustration.SortOrder > firstOrder));
            }
        }
        while (pending.TryDequeue(out var remaining))
            blocks.Add(new DendenIllustrationBlock(remaining));
        return new DendenExportDocument(document, blocks);
    }
}
