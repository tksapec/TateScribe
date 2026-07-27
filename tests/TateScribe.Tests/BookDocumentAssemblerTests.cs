using TateScribe.Core.Export;
using TateScribe.Core.Proofreading;

namespace TateScribe.Tests;

public sealed class BookDocumentAssemblerTests
{
    [Fact]
    public void Source_aware_assembly_tracks_multiple_paragraphs_and_direct_join_across_pages()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = BookDocumentAssembler.AssembleWithSourceSpans([
            new ExportSourcePageText(first, "0001", "甲\n乙", BoundaryJoinType.DirectJoin),
            new ExportSourcePageText(second, "0002", "丙", BoundaryJoinType.DirectJoin),
        ]);

        Assert.Collection(result,
            paragraph =>
            {
                Assert.Equal("甲", paragraph.Paragraph.Text);
                var span = Assert.Single(paragraph.SourceSpans);
                Assert.Equal(first, span.PageId);
                Assert.Equal(1, span.Length);
            },
            paragraph =>
            {
                Assert.Equal("乙丙", paragraph.Paragraph.Text);
                Assert.Collection(paragraph.SourceSpans,
                    span =>
                    {
                        Assert.Equal(first, span.PageId);
                        Assert.Equal(0, span.Start);
                        Assert.Equal(1, span.Length);
                    },
                    span =>
                    {
                        Assert.Equal(second, span.PageId);
                        Assert.Equal(1, span.Start);
                        Assert.Equal(1, span.Length);
                    });
            });
    }

    [Fact]
    public void Source_aware_assembly_preserves_chapter_and_boundary_provenance()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var chapterText = BookDocumentAssembler.CreateChapterPageText("第一話\n章題\n本文");

        var result = BookDocumentAssembler.AssembleWithSourceSpans([
            new ExportSourcePageText(first, "0001", chapterText, BoundaryJoinType.SceneBreak),
            new ExportSourcePageText(second, "0002", "次頁", BoundaryJoinType.DirectJoin),
        ]);

        Assert.Equal(
            BookDocumentAssembler.Assemble([
                new ExportPageText(chapterText, BoundaryJoinType.SceneBreak),
                new ExportPageText("次頁"),
            ]).Paragraphs,
            result.Select(item => item.Paragraph));
        Assert.All(result.Take(4), paragraph =>
            Assert.Equal(first, Assert.Single(paragraph.SourceSpans).PageId));
        Assert.Equal(second, Assert.Single(result[^1].SourceSpans).PageId);
    }

    [Fact]
    public void Assemble_joins_page_text_without_screenshot_boundary_paragraphs()
    {
        var document = BookDocumentAssembler.Assemble(["前ページ", "次ページ"]);

        var paragraph = Assert.Single(document.Paragraphs);
        Assert.Equal(ExportStyle.Normal, paragraph.Style);
        Assert.Equal("前ページ次ページ", paragraph.Text);
    }

    [Fact]
    public void Assemble_turns_detected_paragraph_breaks_into_document_paragraphs()
    {
        var document = BookDocumentAssembler.Assemble(["甲\n乙", "丙"]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal("甲", paragraph.Text),
            paragraph => Assert.Equal("乙丙", paragraph.Text));
    }

    [Fact]
    public void Assemble_preserves_intentional_blank_paragraphs_inside_body_text()
    {
        var document = BookDocumentAssembler.Assemble(["第一段落\n\n第二段落"]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal("第一段落", paragraph.Text),
            paragraph => Assert.Equal(string.Empty, paragraph.Text),
            paragraph => Assert.Equal("第二段落", paragraph.Text));
    }

    [Fact]
    public void Assemble_maps_structure_markers_without_turning_page_markers_into_paragraphs()
    {
        var document = BookDocumentAssembler.Assemble(["""
            [[PAGE:0001]]
            [[CHAPTER:第一章]]
            [[TITLE:始まり]]
            [[SECTION_TITLE:小見出し]]
            [[SECTION:1]]
            本文
            """]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal((ExportStyle.Heading1, DocumentElementRole.ChapterTitle, "第一章"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Heading1, DocumentElementRole.ChapterTitle, "始まり"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Heading2, DocumentElementRole.SectionTitle, "小見出し"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Normal, DocumentElementRole.SectionNumber, "1"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Normal, DocumentElementRole.BodyParagraph, "本文"), (paragraph.Style, paragraph.Role, paragraph.Text)));
    }

    [Fact]
    public void Assemble_keeps_a_chapter_marker_separate_from_the_next_page_text()
    {
        var document = BookDocumentAssembler.Assemble(["[[CHAPTER:第一章]]", "次ページ本文"]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal((ExportStyle.Heading1, DocumentElementRole.ChapterTitle, "第一章"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Normal, DocumentElementRole.BodyParagraph, "次ページ本文"), (paragraph.Style, paragraph.Role, paragraph.Text)));
    }

    [Fact]
    public void Assemble_separates_a_chapter_page_after_a_direct_join_body_page()
    {
        var chapter = BookDocumentAssembler.CreateChapterPageText("Chapter title");

        var document = BookDocumentAssembler.Assemble([
            new ExportPageText("Previous body", BoundaryJoinType.DirectJoin),
            new ExportPageText(chapter, BoundaryJoinType.DirectJoin),
        ]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal(
                (DocumentElementRole.BodyParagraph, "Previous body"),
                (paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal(
                (DocumentElementRole.ChapterTitle, "Chapter title"),
                (paragraph.Role, paragraph.Text)));
    }

    [Theory]
    [InlineData(BoundaryJoinType.DirectJoin)]
    [InlineData(BoundaryJoinType.Uncertain)]
    public void Source_aware_assembly_matches_standard_and_keeps_a_chapter_owned_by_its_page(
        BoundaryJoinType joinType)
    {
        var bodyPage = Guid.NewGuid();
        var chapterPage = Guid.NewGuid();
        var chapter = BookDocumentAssembler.CreateChapterPageText("Chapter title");

        var expected = BookDocumentAssembler.Assemble([
            new ExportPageText("Previous body", joinType),
            new ExportPageText(chapter, BoundaryJoinType.DirectJoin),
        ]);
        var result = BookDocumentAssembler.AssembleWithSourceSpans([
            new ExportSourcePageText(
                bodyPage, "0001", "Previous body", joinType),
            new ExportSourcePageText(
                chapterPage, "0002", chapter, BoundaryJoinType.DirectJoin),
        ]);

        Assert.Equal(expected.Paragraphs, result.Select(item => item.Paragraph));
        Assert.Collection(result,
            paragraph =>
            {
                Assert.Equal(
                    (DocumentElementRole.BodyParagraph, "Previous body"),
                    (paragraph.Paragraph.Role, paragraph.Paragraph.Text));
                Assert.Equal(bodyPage, Assert.Single(paragraph.SourceSpans).PageId);
            },
            paragraph =>
            {
                Assert.Equal(
                    (DocumentElementRole.ChapterTitle, "Chapter title"),
                    (paragraph.Paragraph.Role, paragraph.Paragraph.Text));
                Assert.Equal(chapterPage, Assert.Single(paragraph.SourceSpans).PageId);
            });
    }

    [Fact]
    public void CreateChapterPageText_uses_only_the_first_line_as_the_structural_title()
    {
        var document = BookDocumentAssembler.Assemble([BookDocumentAssembler.CreateChapterPageText("Chapter title\nChapter body")]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal((ExportStyle.Heading1, DocumentElementRole.ChapterTitle, "Chapter title"), (paragraph.Style, paragraph.Role, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Normal, DocumentElementRole.BodyParagraph, "Chapter body"), (paragraph.Style, paragraph.Role, paragraph.Text)));
    }

    [Fact]
    public void Assemble_applies_explicit_page_boundary_join_types_and_preserves_leading_spaces()
    {
        var document = BookDocumentAssembler.Assemble([
            new ExportPageText("文の途中", BoundaryJoinType.DirectJoin),
            new ExportPageText("です", BoundaryJoinType.ParagraphBreak),
            new ExportPageText("　次の段落", BoundaryJoinType.SpaceJoin),
            new ExportPageText("続き", BoundaryJoinType.DirectJoin)
        ]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal("文の途中です", paragraph.Text),
            paragraph => Assert.Equal("　次の段落 続き", paragraph.Text));
    }

    [Fact]
    public void CreateChapterPageText_keeps_a_chapter_number_and_short_title_as_headings()
    {
        var text = BookDocumentAssembler.CreateChapterPageText("第一話\n居眠り八角\n　本文");
        var document = BookDocumentAssembler.Assemble([text]);

        Assert.Collection(document.Paragraphs,
            paragraph => Assert.Equal((ExportStyle.Heading1, "第一話"), (paragraph.Style, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Heading1, "居眠り八角"), (paragraph.Style, paragraph.Text)),
            paragraph => Assert.Equal((ExportStyle.Normal, "　本文"), (paragraph.Style, paragraph.Text)));
    }
}
