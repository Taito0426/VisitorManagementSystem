using System.Text;
using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 履歴照会画面（設計書 8.1節 画面No.3、6.8節）。
/// 入退館履歴とメール送信履歴をタブで切り替えて検索・CSV出力する。
/// DB保持期間（直近3か月）より前の期間を指定した場合はアーカイブCSVの保管場所を案内する。
/// 閲覧範囲はロールに応じて自動的に絞り込まれる（設計書 11.6節）。
/// </summary>
public sealed class HistoryForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly HistoryService _historyService;
    private readonly OperatorContext _ctx;

    private readonly TextBox _visitorNameBox = new() { Width = 140 };
    private readonly TextBox _companyNameBox = new() { Width = 160 };
    private readonly DateTimePicker _dateFromPicker = new() { Width = 120, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly DateTimePicker _dateToPicker = new() { Width = 120, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly ComboBox _siteBox = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly TabControl _tabControl = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _accessGrid = UiTheme.CreateGrid();
    private readonly DataGridView _mailGrid = UiTheme.CreateGrid();
    private readonly BindingSource _accessBinding = [];
    private readonly BindingSource _mailBinding = [];

    private readonly Label _noticeLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 36,
        ForeColor = Color.FromArgb(150, 90, 0),
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 0, 0),
        Visible = false,
    };

    private readonly Button _searchButton = UiTheme.CreateButton("検索");
    private readonly Button _csvButton = UiTheme.CreateButton("CSV出力", 120);
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");
    private readonly Label _countLabel = new() { AutoSize = true, Margin = new Padding(12, 10, 4, 4) };

    public HistoryForm(IServiceProvider provider)
    {
        _provider = provider;
        _historyService = provider.GetRequiredService<HistoryService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - 履歴照会";
        ClientSize = new Size(1180, 620);
        MinimumSize = new Size(960, 520);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        Load += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        var conditionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 10, 12, 6),
        };

        AddCondition(conditionPanel, "来館者氏名", _visitorNameBox);
        AddCondition(conditionPanel, "会社名", _companyNameBox);
        AddCondition(conditionPanel, "期間", _dateFromPicker);
        AddCondition(conditionPanel, "〜", _dateToPicker, labelWidth: 20);
        AddCondition(conditionPanel, "拠点", _siteBox);
        conditionPanel.Controls.Add(_searchButton);

        // --- 入退館履歴タブ ---
        _accessGrid.Columns.AddRange(
            UiTheme.TextColumn("受付日時", nameof(AccessLogRow.AccessDateTimeText), 150),
            UiTheme.TextColumn("拠点", nameof(AccessLogRow.SiteName), 120),
            UiTheme.TextColumn("来館者氏名", nameof(AccessLogRow.VisitorName), 140),
            UiTheme.TextColumn("会社名", nameof(AccessLogRow.CompanyName), 200),
            UiTheme.TextColumn("区分", nameof(AccessLogRow.AccessTypeText), 80),
            UiTheme.TextColumn("端末情報", nameof(AccessLogRow.DeviceInfo), 180),
            UiTheme.TextColumn("備考", nameof(AccessLogRow.OperatorNote), 200, fill: true));
        _accessGrid.DataSource = _accessBinding;

        var accessTab = new TabPage("入退館履歴") { Padding = new Padding(8), BackColor = Color.White };
        accessTab.Controls.Add(_accessGrid);

        // --- メール送信履歴タブ ---
        _mailGrid.Columns.AddRange(
            UiTheme.TextColumn("送信日時", nameof(MailHistoryRow.SentAtText), 150),
            UiTheme.TextColumn("来館者氏名", nameof(MailHistoryRow.VisitorName), 140),
            UiTheme.TextColumn("会社名", nameof(MailHistoryRow.CompanyName), 200),
            UiTheme.TextColumn("メール種別", nameof(MailHistoryRow.MailTypeText), 110),
            UiTheme.TextColumn("宛先区分", nameof(MailHistoryRow.RecipientTypeText), 140),
            UiTheme.TextColumn("送信方式", nameof(MailHistoryRow.ProviderText), 90),
            UiTheme.TextColumn("結果", nameof(MailHistoryRow.StatusText), 70),
            UiTheme.TextColumn("エラー内容", nameof(MailHistoryRow.ErrorMessage), 220, fill: true));
        _mailGrid.DataSource = _mailBinding;

        var mailTab = new TabPage("メール送信履歴") { Padding = new Padding(8), BackColor = Color.White };
        mailTab.Controls.Add(_mailGrid);

        _tabControl.TabPages.AddRange([accessTab, mailTab]);
        _tabControl.SelectedIndexChanged += async (_, _) => await SearchAsync().ConfigureAwait(true);

        var contentPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        contentPanel.Controls.Add(_tabControl);
        contentPanel.Controls.Add(_noticeLabel);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.AddRange([_countLabel, _csvButton, _closeButton]);

        _searchButton.Click += async (_, _) => await SearchAsync().ConfigureAwait(true);
        _csvButton.Click += (_, _) => ExportCsv();
        _closeButton.Click += (_, _) => Close();

        Controls.Add(contentPanel);
        Controls.Add(buttonPanel);
        Controls.Add(conditionPanel);

        AcceptButton = _searchButton;
        CancelButton = _closeButton;
    }

    /// <summary>検索条件の「ラベル＋入力欄」を1組追加する</summary>
    private static void AddCondition(Control parent, string labelText, Control input, int labelWidth = 90)
    {
        var label = new Label
        {
            Text = labelText,
            Width = labelWidth,
            Height = 26,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(4, 6, 2, 4),
        };

        input.Margin = new Padding(2, 4, 10, 4);
        parent.Controls.Add(label);
        parent.Controls.Add(input);
    }

    /// <summary>拠点の選択肢を読み込み、初回検索を行う</summary>
    private async Task InitializeAsync()
    {
        try
        {
            var sites = await _provider.GetRequiredService<MasterDataService>().GetSitesAsync().ConfigureAwait(true);

            _siteBox.Items.Add(new ComboItem<int?>("すべて", null));
            foreach (var site in sites)
            {
                _siteBox.Items.Add(new ComboItem<int?>(site.SiteName, site.SiteId));
            }
            _siteBox.SelectedIndex = 0;

            // 受付ロールは自身の所属拠点に固定される（設計書 11.6節）
            _siteBox.Enabled = !_ctx.IsReception;

            _dateFromPicker.Value = DateTime.Today.AddDays(-7);
            _dateFromPicker.Checked = true;
            _dateToPicker.Value = DateTime.Today;
            _dateToPicker.Checked = true;

            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中のタブに応じて検索を実行する</summary>
    private async Task SearchAsync()
    {
        var dateFrom = _dateFromPicker.Checked ? DateOnly.FromDateTime(_dateFromPicker.Value) : (DateOnly?)null;
        var dateTo = _dateToPicker.Checked ? DateOnly.FromDateTime(_dateToPicker.Value) : (DateOnly?)null;
        var siteId = (_siteBox.SelectedItem as ComboItem<int?>)?.Value;

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                if (_tabControl.SelectedIndex == 0)
                {
                    var logs = await _historyService.SearchAccessLogsAsync(new AccessLogSearchCondition
                    {
                        VisitorName = _visitorNameBox.Text,
                        CompanyName = _companyNameBox.Text,
                        DateFrom = dateFrom,
                        DateTo = dateTo,
                        SiteId = siteId,
                        MaxRows = 5000,
                    }, _ctx).ConfigureAwait(true);

                    _accessBinding.DataSource = logs.Select(l => new AccessLogRow(l)).ToList();
                    _countLabel.Text = $"{logs.Count} 件";
                }
                else
                {
                    var histories = await _historyService.SearchMailHistoryAsync(new MailHistorySearchCondition
                    {
                        VisitorName = _visitorNameBox.Text,
                        CompanyName = _companyNameBox.Text,
                        DateFrom = dateFrom,
                        DateTo = dateTo,
                        SiteId = siteId,
                        MaxRows = 5000,
                    }, _ctx).ConfigureAwait(true);

                    _mailBinding.DataSource = histories.Select(h => new MailHistoryRow(h)).ToList();
                    _countLabel.Text = $"{histories.Count} 件";
                }
            }).ConfigureAwait(true);

            // DB保持期間より前を指定した場合はアーカイブCSVの保管場所を案内する（設計書 6.8節）
            var notice = _historyService.BuildArchiveNotice(dateFrom);
            _noticeLabel.Text = notice ?? string.Empty;
            _noticeLabel.Visible = notice is not null;
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>表示中の履歴をCSVへ出力する</summary>
    private void ExportCsv()
    {
        var isAccessTab = _tabControl.SelectedIndex == 0;
        var defaultName = isAccessTab
            ? $"AccessLogs_{DateTime.Today:yyyyMMdd}.csv"
            : $"MailHistory_{DateTime.Today:yyyyMMdd}.csv";

        using var dialog = new SaveFileDialog
        {
            Title = "CSV出力",
            Filter = "CSVファイル (*.csv)|*.csv",
            FileName = defaultName,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var csv = isAccessTab
                ? HistoryService.ToCsv([.. ((List<AccessLogRow>)_accessBinding.DataSource!).Select(r => r.Source)])
                : HistoryService.ToCsv([.. ((List<MailHistoryRow>)_mailBinding.DataSource!).Select(r => r.Source)]);

            // Excel で直接開けるよう BOM 付き UTF-8 で出力する
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            UiTheme.ShowInfo(this, $"CSVファイルを出力しました。{Environment.NewLine}{dialog.FileName}");
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>入退館履歴の表示行</summary>
    private sealed class AccessLogRow(AccessLog source)
    {
        public AccessLog Source { get; } = source;

        public string AccessDateTimeText => HistoryService.ToLocalText(Source.AccessDateTime);

        public string? SiteName => Source.SiteName;

        public string? VisitorName => Source.VisitorName;

        public string? CompanyName => Source.CompanyName;

        public string AccessTypeText => HistoryService.ToText(Source.AccessType);

        public string? DeviceInfo => Source.DeviceInfo;

        public string? OperatorNote => Source.OperatorNote;
    }

    /// <summary>メール送信履歴の表示行</summary>
    private sealed class MailHistoryRow(MailHistory source)
    {
        public MailHistory Source { get; } = source;

        public string SentAtText => HistoryService.ToLocalText(Source.SentAt);

        public string? VisitorName => Source.VisitorName;

        public string? CompanyName => Source.CompanyName;

        public string MailTypeText => HistoryService.ToText(Source.MailType);

        public string RecipientTypeText => HistoryService.ToText(Source.RecipientType);

        public string ProviderText => Source.Provider == MailProviderType.SendGrid ? "SendGrid" : "SMTP";

        public string StatusText => Source.Status == MailSendStatus.Succeeded ? "成功" : "失敗";

        public string? ErrorMessage => Source.ErrorMessage;
    }
}
