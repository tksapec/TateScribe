# ChatGPT final proofreading prompt

## Goal

Let the user copy a ready-to-use instruction for asking ChatGPT to perform the
final proofreading pass on a DOCX file produced from OCR.

## User interface

Add a `ChatGPT最終校正用の指示` button near `DOCXへ出力` in the left pane.
The button opens a separate modal window so the long instruction does not
consume space in the main window.

The modal window contains:

- an editable multiline text box initialized with the standard instruction;
- a `クリップボードにコピー` button;
- a `閉じる` button;
- a short status message after a successful copy.

The user may edit or append project-specific notes before copying. Closing the
window does not persist those edits.

## Standard instruction

The initial text tells ChatGPT that:

- the attached DOCX was created by OCR from vertical Japanese book screens;
- typographical errors, missing text, mojibake, and punctuation errors may be
  present;
- the full document should be proofread without unnecessarily changing the
  original meaning, style, or proper nouns;
- uncertain or ambiguous passages must not be finalized by guessing and should
  instead be confirmed with the user;
- the result must be returned as a corrected DOCX file.

## Components

- `FinalProofreadingPrompt` in the Core project owns the standard instruction,
  keeping the wording testable without WPF.
- `ChatGptProofreadingPromptWindow` in the App project displays the instruction
  and performs the clipboard copy.
- `MainWindow` opens the prompt window from the new button.

## Error handling

If the clipboard is temporarily unavailable, keep the window open and show a
clear error message. The instruction text must not be lost.

## Verification

- Unit-test that the standard instruction includes the OCR origin, uncertainty
  rule, user-confirmation request, and corrected-DOCX return requirement.
- Add a source-shape test for the main-window button and prompt window controls.
- Run the complete .NET and Python test suites.
- Build the WPF application in Release configuration with zero errors.
