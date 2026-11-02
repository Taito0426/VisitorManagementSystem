using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.Infrastructure.Mail;

/// <summary>
/// デモ版のメール送信モック（設計書 19.2節）。
/// 実際の外部送信は行わず、送信内容をメモリ上に保持する。
/// 送信結果は呼び出し側（サービス層）が MailHistory へ記録し、履歴照会画面から確認できる。
/// </summary>
public sealed class DemoMailSender(
    IOptions<MailSettings> mailOptions,
    ILogger<DemoMailSender> logger) : MailSenderBase
{
    /// <summary>デモ中に保持する送信済みメールの最大件数</summary>
    private const int MaxRetainedMessages = 200;

    private readonly ConcurrentQueue<DemoSentMail> _sentMails = new();

    /// <summary>
    /// MailHistory.Provider には設定上のメール方式を記録する
    /// （デモ用の値はDB制約上存在しないため、Mail:Provider の設定値を使用する）。
    /// </summary>
    public override MailProviderType ProviderType =>
        string.Equals(mailOptions.Value.Provider, MailProviderNames.Smtp, StringComparison.OrdinalIgnoreCase)
            ? MailProviderType.Smtp
            : MailProviderType.SendGrid;

    /// <inheritdoc />
    /// <remarks>実際の外部送信は行わないため常に true。画面の送信結果は「テスト」と表示される。</remarks>
    public override bool IsTestMode => true;

    /// <summary>デモ中に「送信」されたメールの一覧（新しい順）</summary>
    public IReadOnlyList<DemoSentMail> SentMails => [.. _sentMails.Reverse()];

    /// <inheritdoc />
    protected override Task SendCoreAsync(string to, string subject, string plainTextBody, MailAttachment? attachment)
    {
        _sentMails.Enqueue(new DemoSentMail(DateTime.UtcNow, to, subject, plainTextBody, attachment?.Content.Length ?? 0));

        // 保持件数の上限を超えた分は古いものから破棄する
        while (_sentMails.Count > MaxRetainedMessages && _sentMails.TryDequeue(out _))
        {
        }

        logger.LogInformation("デモ版のためメールは実送信されません。宛先: {To} / 件名: {Subject}", to, subject);
        return Task.CompletedTask;
    }

    /// <summary>保持している送信済みメールをすべて破棄する（サンプルデータ初期化時に使用）</summary>
    public void Clear()
    {
        while (_sentMails.TryDequeue(out _))
        {
        }
    }
}

/// <summary>
/// デモ版で「送信」されたメールの記録。
/// </summary>
/// <param name="SentAtUtc">送信日時（UTC）</param>
/// <param name="To">宛先</param>
/// <param name="Subject">件名</param>
/// <param name="Body">本文（プレーンテキスト）</param>
/// <param name="AttachmentBytes">添付ファイルのバイト数</param>
public sealed record DemoSentMail(DateTime SentAtUtc, string To, string Subject, string Body, int AttachmentBytes);
