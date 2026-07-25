using TateScribe.Core.Ocr;

namespace TateScribe.Core.Projects;

public sealed record PageTextState(
    Guid PageId,
    string? ManualText,
    string Engine,
    string ModelVersion,
    IReadOnlyList<OcrWord> MachineWords);
