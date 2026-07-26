namespace TateScribe.Core.ChatGpt;

public enum ChatGptTaskType
{
    TextProofreading,
    RubyAnnotation,
}

public interface IChatGptPromptTemplateProvider
{
    string GetTemplate(ChatGptTaskType taskType);
}

public sealed class ChatGptPromptTemplateProvider : IChatGptPromptTemplateProvider
{
    public string GetTemplate(ChatGptTaskType taskType) => taskType switch
    {
        ChatGptTaskType.TextProofreading => TextProofreadingTemplate,
        ChatGptTaskType.RubyAnnotation => RubyAnnotationTemplate,
        _ => throw new ArgumentOutOfRangeException(nameof(taskType), taskType, null),
    };

    private const string TextProofreadingTemplate = """
        添付したTateScribe校正用パッケージの画像を正本として、
        ocr.txtの本文を校正してください。

        重要:
        1. DOCX、EPUB、Markdown等の完成ファイルを作成しないでください。
        2. 返却するのはTateScribeへ再取込み可能な構造化テキストだけです。
        3. [[TATESCRIBE_FORMAT:2]]、PROJECT_ID、BATCH_IDを変更しないでください。
        4. PAGEマーカーを削除、追加、並べ替えしないでください。
        5. 校正済み本文は各PAGEの[[TEXT_BEGIN]]と[[TEXT_END]]の間だけに記入してください。
        6. [[JOIN_TO_NEXT:...]]を変更する必要がある場合は、画像上の段落・文章の継続関係を確認してください。
        7. 判読不能箇所と主な修正内容は[[REPORT_BEGIN]]と[[REPORT_END]]の間に記入してください。
        8. 報告文をTEXT_BEGIN／TEXT_END内へ入れないでください。
        9. OCR本文だけから推測せず、必ず対応する画像を確認してください。
        10. 縦書きは右列から左列へ読んでください。
        11. 列の切替わりやスクリーンショット境界だけを理由に改行しないでください。
        12. 原文の段落、会話文、鉤括弧、句読点、全角空白を保持してください。
        13. 原文にない表現を追加せず、読みやすさを目的とする書換えをしないでください。
        14. ルビを本文へ追加しないでください。
        15. ステータスバー、進捗率、ページ番号、柱見出し、写真、挿絵、キャプションは本文へ追加しないでください。
        16. 判断できない文字は推測で確定せず、［判読不能: PAGE-xxxx］としてください。
        17. 出力全体を1つのコードブロックに入れても構いませんが、コードブロック以外の説明文は付けないでください。
        """;

    private const string RubyAnnotationTemplate = """
        添付したTateScribeルビ確認用パッケージを確認し、
        校正済み本文に対応するルビ注釈を作成してください。

        最重要事項:
        1. 本文の文字、句読点、空白、改行、段落、章・節構造を一切変更しないでください。
        2. DOCX、EPUB、Markdown等の完成ファイルを作成しないでください。
        3. 本文全体を再出力しないでください。
        4. 指定されたJSONスキーマに従ったJSONだけを返してください。
        5. JSONの前後に説明文やMarkdownコードフェンスを付けないでください。

        識別情報とJSON形式:
        1. formatVersionには1を指定してください。
        2. manifest.jsonのprojectIdを一字も変更せず、そのまま出力してください。
        3. manifest.jsonのbatchIdを一字も変更せず、そのまま出力してください。
        4. manifest.jsonのdocumentTextHashを一字も変更せず、そのまま出力してください。
        5. annotationsとunresolvedは、候補がなくても空配列として必ず出力してください。
        6. output-schema.jsonのプロパティ名を変更しないでください。
        7. output-schema.jsonにない未知のプロパティを追加しないでください。

        ルビの判断規則:
        1. 原文画像に実際のルビがある場合は、画像を根拠としてImageConfirmedとしてください。
        2. 本文中に「本来ヤスミと読む」等、読みが明記されている場合はTextConfirmedとしてください。
        3. ユーザーが事前に確定した辞書情報はUserConfirmedとしてください。
        4. 一般的な辞書や文脈だけから推測した読みは確定せず、DictionarySuggestedまたはContextSuggestedとしてください。
        5. 固有名詞等の読みを画像または本文で確認できない場合はUnresolvedへ入れてください。
        6. 同じ表記でも、出現場所によって読みが異なる可能性があります。
        7. 親文字の範囲を必要以上に広げないでください。
        8. 本文に存在しない親文字を作成しないでください。
        9. 原書にない総ルビを勝手に追加しないでください。
        10. rubyPolicyに従ってください。

        OCR候補の扱い:
        1. ruby-candidates.jsonのreadingCandidateはルビの読み候補です。
        2. baseTextCandidateは親文字候補です。
        3. readingCandidateを親文字として扱わないでください。
        4. baseTextCandidateは参考情報であり、confirmed-document.jsonの本文範囲を正本としてください。
        5. OCR候補が不明確な場合は対応する画像を確認してください。
        6. 画像で読みを確定できない場合はunresolvedへ入れてください。

        位置指定:
        - paragraphIdはconfirmed-document.jsonの値をそのまま使用し、変更しないでください。
        - startとlengthはUTF-16コード単位で指定してください。
        - baseTextは指定範囲の本文と完全一致させてください。
        - 同じ文字列が複数ある場合もstartで個別に指定してください。

        sourceに使用できる値:
        - ImageConfirmed
        - TextConfirmed
        - UserConfirmed
        - DictionarySuggested
        - ContextSuggested

        確定できない読みはannotationsへ入れず、unresolvedへ入れてください。
        """;
}
