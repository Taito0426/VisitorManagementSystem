using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// システム設定確認画面（設計書 8.1節 画面No.7、6.13節）。管理者ロール専用・読取専用。
/// DB接続方式・メール送信方式などの現在値を表示する。
/// 値の変更は設定ファイル編集＋アプリケーション再起動を要し、非稼働日に切替バッチを実行する運用とする
/// （設計書 13.3節。稼働中のシームレスな切替はサポート対象外）。
/// </summary>
public sealed class SystemSettingsForm : Form
{
    private readonly SystemSettingsService _systemSettingsService;
    private readonly OperatorContext _ctx;

    private readonly TextBox _contentBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 10F),
        BackColor = Color.FromArgb(250, 250, 250),
        BorderStyle = BorderStyle.FixedSingle,
    };

    public SystemSettingsForm(IServiceProvider provider)
    {
        _systemSettingsService = provider.GetRequiredService<SystemSettingsService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - システム設定確認";
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(640, 480);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        LoadSettings();
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        var noticeLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Text = "本画面は読取専用です。設定値の変更は appsettings.json を編集し、アプリケーションを再起動してください。"
                 + Environment.NewLine
                 + "DB方式・メール方式の切替は非稼働日にバッチで実施します（稼働中の切替はサポート対象外）。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = Color.FromArgb(150, 90, 0),
        };

        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        contentPanel.Controls.Add(_contentBox);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
            FlowDirection = FlowDirection.RightToLeft,
        };

        var closeButton = UiTheme.CreateButton("閉じる");
        closeButton.Click += (_, _) => Close();
        buttonPanel.Controls.Add(closeButton);

        Controls.Add(contentPanel);
        Controls.Add(buttonPanel);
        Controls.Add(noticeLabel);

        CancelButton = closeButton;
    }

    /// <summary>現在の設定値を読み込んで表示する</summary>
    private void LoadSettings()
    {
        try
        {
            var settings = _systemSettingsService.GetCurrentSettings(_ctx);
            var sb = new StringBuilder();

            AppendSection(sb, "データベース");
            AppendItem(sb, "DB方式", $"{settings.DatabaseProvider}（{settings.DatabaseProviderDisplayName}）");
            AppendItem(sb, "リトライ方針", settings.RetryPolicyDescription);
            AppendItem(sb, "バックアップ運用", settings.BackupPolicyDescription);

            AppendSection(sb, "メール送信");
            AppendItem(sb, "送信方式", settings.MailProvider);
            AppendItem(sb, "使用中の実装", settings.MailSenderTypeName);
            AppendItem(sb, "差出人アドレス", settings.MailFromAddress);

            AppendSection(sb, "ラベル印刷");
            AppendItem(sb, "印刷方式", settings.PrintingProvider);
            foreach (var preset in settings.LabelPresets)
            {
                var value = preset.IsDefined
                    ? $"幅{preset.WidthMm}mm × 高さ{preset.HeightMm}mm"
                      + (string.IsNullOrWhiteSpace(preset.LabelPartNumber) ? string.Empty : $"（{preset.LabelPartNumber}）")
                    : "未設定（導入時ヒアリングにより個別設定）";

                AppendItem(sb, $"プリセット{(int)preset.SizeType}", value);
            }

            AppendSection(sb, "ログアーカイブ");
            AppendItem(sb, "DB保持期間", $"{settings.LogArchiveRetentionMonths} か月");
            AppendItem(sb, "保管先種別", settings.LogArchiveStorageMode);
            AppendItem(sb, "保管場所", settings.LogArchiveLocation);

            AppendSection(sb, "デモ");
            AppendItem(sb, "デモモード", settings.DemoEnabled ? "有効" : "無効");

            _contentBox.Text = sb.ToString();
            // 読取専用テキストボックスの全選択状態を解除する
            _contentBox.SelectionStart = 0;
            _contentBox.SelectionLength = 0;
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
            Close();
        }
    }

    /// <summary>セクション見出しを追記する</summary>
    private static void AppendSection(StringBuilder sb, string title)
    {
        if (sb.Length > 0)
        {
            sb.AppendLine();
        }

        sb.AppendLine($"■ {title}");
        sb.AppendLine(new string('-', 70));
    }

    /// <summary>設定項目を追記する</summary>
    private static void AppendItem(StringBuilder sb, string label, string value)
        => sb.AppendLine($"  {label,-16} : {value}");
}
