using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 来館予約管理画面（設計書 8.1節 画面No.2）。
/// 予約グループ（代表者行）の一覧・検索、登録・編集・キャンセル・削除、
/// およびメール再送信（設計書 6.7節）を提供する。
/// 閲覧範囲はロールに応じて自動的に絞り込まれる（設計書 11.6節）。
/// </summary>
public sealed class ReservationListForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly ReservationService _reservationService;
    private readonly OperatorContext _ctx;

    private readonly TextBox _visitorNameBox = new() { Width = 140 };
    private readonly TextBox _companyNameBox = new() { Width = 160 };
    private readonly DateTimePicker _dateFromPicker = new() { Width = 120, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly DateTimePicker _dateToPicker = new() { Width = 120, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly ComboBox _statusBox = new() { Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _siteBox = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };

    private readonly DataGridView _grid = UiTheme.CreateGrid();
    private readonly BindingSource _bindingSource = [];
    private readonly Label _companionLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(80, 80, 80) };

    private readonly Button _searchButton = UiTheme.CreateButton("検索");
    private readonly Button _newButton = UiTheme.CreateButton("新規登録");
    private readonly Button _editButton = UiTheme.CreateButton("編集");
    private readonly Button _resendButton = UiTheme.CreateButton("メール再送信", 130);
    private readonly Button _cancelReservationButton = UiTheme.CreateButton("キャンセル");
    private readonly Button _deleteButton = UiTheme.CreateButton("削除");
    private readonly Button _qrButton = UiTheme.CreateButton("QRコード表示", 130);
    private readonly Button _layoutPdfButton = UiTheme.CreateButton("PDFレイアウト確認", 160);
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");

    /// <summary>レイアウト確認用のPDF生成（デモ版構成またはDebugビルドでのみ解決できる）</summary>
    private readonly ILabelPdfPreview? _labelPdfPreview;

    public ReservationListForm(IServiceProvider provider)
    {
        _provider = provider;
        _reservationService = provider.GetRequiredService<ReservationService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;
        _labelPdfPreview = provider.GetService<ILabelPdfPreview>();

        Text = $"{UiTheme.AppName} - 来館予約管理";
        ClientSize = new Size(1180, 620);
        MinimumSize = new Size(1000, 520);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();
        ApplyPermissions();

        Load += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        // --- 検索条件 ---
        var conditionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            Padding = new Padding(12, 10, 12, 6),
            AutoSize = false,
        };

        AddCondition(conditionPanel, "来館者氏名", _visitorNameBox);
        AddCondition(conditionPanel, "会社名", _companyNameBox);
        AddCondition(conditionPanel, "来館日", _dateFromPicker);
        AddCondition(conditionPanel, "〜", _dateToPicker, labelWidth: 20);
        AddCondition(conditionPanel, "ステータス", _statusBox);
        AddCondition(conditionPanel, "拠点", _siteBox);
        conditionPanel.Controls.Add(_searchButton);

        // --- 一覧 ---
        _grid.Columns.AddRange(
            UiTheme.TextColumn("来館日", nameof(ReservationRow.VisitDateText), 100),
            UiTheme.TextColumn("時刻", nameof(ReservationRow.VisitTimeText), 110),
            UiTheme.TextColumn("拠点", nameof(ReservationRow.SiteName), 120),
            UiTheme.TextColumn("会社名", nameof(ReservationRow.CompanyName), 180),
            UiTheme.TextColumn("代表者氏名", nameof(ReservationRow.VisitorName), 140),
            UiTheme.TextColumn("同行者", nameof(ReservationRow.CompanionText), 80),
            UiTheme.TextColumn("種別", nameof(ReservationRow.ReservationTypeText), 80),
            UiTheme.TextColumn("ステータス", nameof(ReservationRow.StatusText), 90),
            UiTheme.TextColumn("訪問先担当者", nameof(ReservationRow.HostEmployeeName), 130),
            UiTheme.TextColumn("登録者", nameof(ReservationRow.CreatedByDisplayName), 120, fill: true));

        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += async (_, _) => await UpdateSelectionAsync().ConfigureAwait(true);
        _grid.CellDoubleClick += async (_, e) =>
        {
            if (e.RowIndex >= 0 && _editButton.Enabled)
            {
                await EditAsync().ConfigureAwait(true);
            }
        };

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        gridPanel.Controls.Add(_grid);

        // --- 操作ボタン ---
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 84,
            Padding = new Padding(12, 8, 12, 8),
            FlowDirection = FlowDirection.LeftToRight,
        };

        _companionLabel.Margin = new Padding(4, 8, 4, 8);
        buttonPanel.Controls.Add(_companionLabel);
        buttonPanel.SetFlowBreak(_companionLabel, true);
        buttonPanel.Controls.AddRange([
            _newButton, _editButton, _qrButton, _layoutPdfButton,
            _resendButton, _cancelReservationButton, _deleteButton, _closeButton]);

        _searchButton.Click += async (_, _) => await SearchAsync().ConfigureAwait(true);
        _newButton.Click += async (_, _) => await CreateAsync().ConfigureAwait(true);
        _editButton.Click += async (_, _) => await EditAsync().ConfigureAwait(true);
        _qrButton.Click += async (_, _) => await ShowQrCodeAsync().ConfigureAwait(true);
        _layoutPdfButton.Click += async (_, _) => await ShowLayoutPdfAsync().ConfigureAwait(true);
        _resendButton.Click += async (_, _) => await ResendMailAsync().ConfigureAwait(true);
        _cancelReservationButton.Click += async (_, _) => await CancelReservationAsync().ConfigureAwait(true);
        _deleteButton.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();

        Controls.Add(gridPanel);
        Controls.Add(buttonPanel);
        Controls.Add(conditionPanel);

        AcceptButton = _searchButton;
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

    /// <summary>権限ロールに応じてボタンの活性状態を設定する（設計書 11.6節）</summary>
    private void ApplyPermissions()
    {
        // 一般ロールも自分が登録した予約であれば登録・編集・キャンセルを行える。
        // 削除とメール再送信は管理者・受付のみ。
        _newButton.Enabled = PermissionPolicy.CanCreateReservation(_ctx);
        _editButton.Enabled = false;
        _resendButton.Enabled = false;
        _cancelReservationButton.Enabled = false;
        _deleteButton.Enabled = false;
        _qrButton.Enabled = false;
        _layoutPdfButton.Enabled = false;

        // レイアウト確認用のPDF出力はデモ版構成・Debugビルドでのみ提供する
        _layoutPdfButton.Visible = _labelPdfPreview is not null;

        _newButton.Visible = PermissionPolicy.CanCreateReservation(_ctx);
        _editButton.Visible = PermissionPolicy.CanEditReservation(_ctx);
        _resendButton.Visible = PermissionPolicy.CanResendMail(_ctx);
        _cancelReservationButton.Visible = PermissionPolicy.CanEditReservation(_ctx);
        _deleteButton.Visible = PermissionPolicy.CanDeleteReservation(_ctx);
    }

    /// <summary>拠点・ステータスの選択肢を読み込み、初回検索を行う</summary>
    private async Task InitializeAsync()
    {
        try
        {
            _statusBox.Items.Add(new ComboItem<VisitorStatus?>("すべて", null));
            foreach (var status in new[]
            {
                VisitorStatus.NotArrived, VisitorStatus.CheckedIn, VisitorStatus.CheckedOut, VisitorStatus.Cancelled,
            })
            {
                _statusBox.Items.Add(new ComboItem<VisitorStatus?>(HistoryService.ToText(status), status));
            }
            _statusBox.SelectedIndex = 0;

            await LoadSitesAsync().ConfigureAwait(true);

            // 既定は当日分を表示する
            _dateFromPicker.Value = DateTime.Today;
            _dateFromPicker.Checked = true;
            _dateToPicker.Value = DateTime.Today;
            _dateToPicker.Checked = false;

            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>拠点の選択肢を読み込む（Entryプラン＝1拠点構成では選択UIを隠す）</summary>
    private async Task LoadSitesAsync()
    {
        var masterDataService = _provider.GetRequiredService<MasterDataService>();
        var sites = await masterDataService.GetSitesAsync().ConfigureAwait(true);

        _siteBox.Items.Clear();
        _siteBox.Items.Add(new ComboItem<int?>("すべて", null));
        foreach (var site in sites)
        {
            _siteBox.Items.Add(new ComboItem<int?>(site.SiteName, site.SiteId));
        }
        _siteBox.SelectedIndex = 0;

        // 単一拠点構成では拠点選択UIを非表示にする（設計書 6.11節）
        var isSingleSite = sites.Count <= 1;
        _siteBox.Visible = !isSingleSite;
        if (_siteBox.Parent is not null)
        {
            var index = _siteBox.Parent.Controls.GetChildIndex(_siteBox);
            if (index > 0 && _siteBox.Parent.Controls[index - 1] is Label siteLabel)
            {
                siteLabel.Visible = !isSingleSite;
            }
        }

        // 受付ロールは自身の所属拠点に固定されるため選択を無効化する
        if (_ctx.IsReception)
        {
            _siteBox.Enabled = false;
        }
    }

    /// <summary>検索を実行する</summary>
    private async Task SearchAsync()
    {
        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var condition = new VisitorSearchCondition
                {
                    VisitorName = _visitorNameBox.Text,
                    CompanyName = _companyNameBox.Text,
                    VisitDateFrom = _dateFromPicker.Checked ? DateOnly.FromDateTime(_dateFromPicker.Value) : null,
                    VisitDateTo = _dateToPicker.Checked ? DateOnly.FromDateTime(_dateToPicker.Value) : null,
                    SiteId = (_siteBox.SelectedItem as ComboItem<int?>)?.Value,
                    Status = (_statusBox.SelectedItem as ComboItem<VisitorStatus?>)?.Value,
                    MaxRows = 1000,
                };

                // 一覧は予約グループ単位で表示するため代表者行のみを取得する
                condition.RepresentativeOnly = true;

                var representatives = await _reservationService.SearchAsync(condition, _ctx).ConfigureAwait(true);

                // 同行者人数を表示するため、同一検索条件で全行も取得して集計する
                var allCondition = CloneWithoutRepresentativeOnly(condition);
                var all = await _reservationService.SearchAsync(allCondition, _ctx).ConfigureAwait(true);
                var companionCounts = all
                    .Where(v => !v.IsRepresentative)
                    .GroupBy(v => v.ReservationGroupId)
                    .ToDictionary(g => g.Key, g => g.Count());

                _bindingSource.DataSource = representatives
                    .Select(v => new ReservationRow(v, companionCounts.GetValueOrDefault(v.ReservationGroupId)))
                    .ToList();
            }).ConfigureAwait(true);

            await UpdateSelectionAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>代表者限定フラグを外した検索条件を複製する</summary>
    private static VisitorSearchCondition CloneWithoutRepresentativeOnly(VisitorSearchCondition source) => new()
    {
        VisitorName = source.VisitorName,
        CompanyName = source.CompanyName,
        VisitDateFrom = source.VisitDateFrom,
        VisitDateTo = source.VisitDateTo,
        SiteId = source.SiteId,
        Status = source.Status,
        MaxRows = source.MaxRows,
        RepresentativeOnly = false,
    };

    /// <summary>選択行に応じてボタンの活性状態と同行者情報を更新する</summary>
    private async Task UpdateSelectionAsync()
    {
        var row = SelectedRow;

        // 一般ロールは自分が登録した予約のみ操作できる（一覧には自分の予約しか出ないが、念のため行単位でも判定する）
        var canOperate = row is not null
            && PermissionPolicy.CanEditReservation(_ctx)
            && PermissionPolicy.CanOperateOnReservation(_ctx, row.Visitor);

        _editButton.Enabled = canOperate;
        _qrButton.Enabled = row is not null;
        _layoutPdfButton.Enabled = _labelPdfPreview is not null && row is not null;
        _cancelReservationButton.Enabled = canOperate && VisitRulesCanCancel(row!);
        _deleteButton.Enabled = PermissionPolicy.CanDeleteReservation(_ctx)
            && row is not null
            && row.Visitor.Status == VisitorStatus.NotArrived;

        // 再送信の可否は設計書 5.3節・6.7節のルールに従う
        _resendButton.Enabled = PermissionPolicy.CanResendMail(_ctx)
            && row is not null
            && Core.VisitRules.CanResendMail(row.Visitor.Status, row.Visitor.ReservationType);

        if (row is null)
        {
            _companionLabel.Text = string.Empty;
            return;
        }

        try
        {
            var group = await _reservationService.GetGroupAsync(row.Visitor.ReservationGroupId).ConfigureAwait(true);
            _companionLabel.Text = group is null || group.Companions.Count == 0
                ? "同行者なし（QRコードは代表者に1件のみ発行されます）"
                : "同行者: " + string.Join("、", group.Companions.Select(c => c.VisitorName));
        }
        catch (Exception)
        {
            _companionLabel.Text = string.Empty;
        }
    }

    /// <summary>キャンセル可能かを判定する</summary>
    private static bool VisitRulesCanCancel(ReservationRow row) => Core.VisitRules.CanCancel(row.Visitor.Status);

    /// <summary>新規登録する</summary>
    private async Task CreateAsync()
    {
        using var form = new ReservationEditForm(_provider, null);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    /// <summary>選択中の予約を編集する</summary>
    private async Task EditAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        using var form = new ReservationEditForm(_provider, row.Visitor.ReservationGroupId);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    /// <summary>選択中の予約のQRコードを表示する</summary>
    private async Task ShowQrCodeAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        try
        {
            var group = await _reservationService.GetGroupAsync(row.Visitor.ReservationGroupId).ConfigureAwait(true);
            if (group?.Token is null)
            {
                UiTheme.ShowWarning(this, "この予約にはQRコードが発行されていません。");
                return;
            }

            var png = _reservationService.GenerateQrCodePng(group.Token);
            using var form = new QrCodePreviewForm(_provider, group, png);
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>
    /// 選択中の予約について、ラベルとメール文面のレイアウトをA4 PDFへ出力して開く（設計書 19.4節）。
    /// デモ・商談でのレイアウト提示と、開発時の確認を兼ねた機能。
    /// </summary>
    private async Task ShowLayoutPdfAsync()
    {
        var row = SelectedRow;
        if (row is null || _labelPdfPreview is null)
        {
            return;
        }

        try
        {
            _layoutPdfButton.Enabled = false;

            var group = await _reservationService.GetGroupAsync(row.Visitor.ReservationGroupId).ConfigureAwait(true);
            if (group?.Token is null)
            {
                UiTheme.ShowWarning(this, "この予約にはQRコードが発行されていないため、レイアウトを確認できません。");
                return;
            }

            var site = await _provider.GetRequiredService<MasterDataService>()
                .GetSiteAsync(group.Representative.SiteId).ConfigureAwait(true);

            if (site is null)
            {
                UiTheme.ShowWarning(this, "来館先拠点の情報を取得できないため、PDFを出力できません。");
                return;
            }

            var qrCodePng = _reservationService.GenerateQrCodePng(group.Token);

            byte[]? pdf = null;
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                pdf = await _labelPdfPreview.GeneratePreviewPdfAsync(group, qrCodePng, site).ConfigureAwait(true))
                .ConfigureAwait(true);

            var filePath = Path.Combine(
                Path.GetTempPath(), $"layout-preview-{group.ReservationGroupId:N}.pdf");
            await File.WriteAllBytesAsync(filePath, pdf!).ConfigureAwait(true);

            // 関連付けられたPDFビューアで開く（未関連付けの場合は保存先を案内する）
            try
            {
                using var process = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                UiTheme.ShowInfo(this, $"PDFを出力しました。{Environment.NewLine}{filePath}");
            }
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
        finally
        {
            _layoutPdfButton.Enabled = SelectedRow is not null;
        }
    }

    /// <summary>来館案内メールを再送信する（設計書 6.7節）</summary>
    private async Task ResendMailAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        if (!UiTheme.Confirm(this,
            $"「{row.Visitor.VisitorName}」様および予約登録者へ来館案内メールを再送信します。よろしいですか？"))
        {
            return;
        }

        try
        {
            IReadOnlyList<MailDeliveryResult> results = [];
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                results = await _reservationService
                    .ResendMailAsync(row.Visitor.ReservationGroupId, _ctx).ConfigureAwait(true);
            }).ConfigureAwait(true);

            var summary = results.Count == 0
                ? "送信先が設定されていないため、送信は行われませんでした。"
                : string.Join(Environment.NewLine, results.Select(r =>
                    $"・{HistoryService.ToText(r.RecipientType)}（{r.Address}）: {r.ToDisplayText()}"));

            // テストモード（デモ版・Debugビルド）では実送信していないことを明示する
            if (results.Any(r => r.IsTest))
            {
                summary += Environment.NewLine
                        + "※ テストモードのため外部へのメール送信は行っていません（送信履歴には記録されます）。";
            }

            UiTheme.ShowInfo(this, "メール再送信を実行しました。" + Environment.NewLine + Environment.NewLine + summary);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の予約をキャンセルする</summary>
    private async Task CancelReservationAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        if (!UiTheme.Confirm(this,
            $"「{row.Visitor.VisitorName}」様の来館予約をキャンセルします。"
            + Environment.NewLine + "QRコードは無効化され、招待者へキャンセル通知メールが送信されます。よろしいですか？"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _reservationService.CancelAsync(row.Visitor.ReservationGroupId, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の予約を削除する</summary>
    private async Task DeleteAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        if (!UiTheme.Confirm(this,
            $"「{row.Visitor.VisitorName}」様の来館予約を削除します。この操作は取り消せません。よろしいですか？"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _reservationService.DeleteAsync(row.Visitor.ReservationGroupId, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の行</summary>
    private ReservationRow? SelectedRow
        => _grid.CurrentRow?.DataBoundItem as ReservationRow;

    /// <summary>
    /// 一覧表示用の行データ。予約グループ単位（代表者1行）で表示する。
    /// </summary>
    private sealed class ReservationRow(Visitor visitor, int companionCount)
    {
        /// <summary>代表者の予約エンティティ</summary>
        public Visitor Visitor { get; } = visitor;

        public string VisitDateText => Visitor.VisitDate.ToString("yyyy/MM/dd");

        public string VisitTimeText => Visitor.VisitTimeTo.HasValue
            ? $"{Visitor.VisitTimeFrom:HH\\:mm} - {Visitor.VisitTimeTo.Value:HH\\:mm}"
            : $"{Visitor.VisitTimeFrom:HH\\:mm}";

        public string? SiteName => Visitor.SiteName;

        public string? CompanyName => Visitor.CompanyName;

        public string VisitorName => Visitor.VisitorName;

        public string CompanionText => companionCount == 0 ? "-" : $"+{companionCount}名";

        public string ReservationTypeText => Visitor.ReservationType == ReservationType.Advance ? "事前予約" : "当日受付";

        public string StatusText => HistoryService.ToText(Visitor.Status);

        public string? HostEmployeeName => Visitor.HostEmployeeName;

        public string? CreatedByDisplayName => Visitor.CreatedByDisplayName;
    }
}

/// <summary>
/// コンボボックス用の「表示名と値」の組。
/// </summary>
public sealed class ComboItem<T>(string text, T value)
{
    /// <summary>値</summary>
    public T Value { get; } = value;

    /// <inheritdoc />
    public override string ToString() => text;
}
