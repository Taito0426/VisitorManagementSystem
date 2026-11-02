using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 来館予約 登録・編集画面（設計書 8.1節 画面No.2、6.1節）。
/// 1件の予約に代表者1名＋任意の同行者を登録できる。
/// 登録完了時にQRトークンを1件（代表者のみ）発行し、来館案内メールを送信する。
/// </summary>
public sealed class ReservationEditForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly ReservationService _reservationService;
    private readonly OperatorContext _ctx;
    private readonly Guid? _reservationGroupId;

    private readonly ComboBox _siteBox = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _visitDatePicker = new() { Width = 130, Format = DateTimePickerFormat.Short };
    private readonly DateTimePicker _timeFromPicker = new() { Width = 100, Format = DateTimePickerFormat.Time, ShowUpDown = true };
    private readonly DateTimePicker _timeToPicker = new() { Width = 100, Format = DateTimePickerFormat.Time, ShowUpDown = true, ShowCheckBox = true };
    private readonly TextBox _hostBox = new() { Width = 240 };
    private readonly TextBox _purposeBox = new() { Width = 400 };

    private readonly ComboBox _companyBox = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDown, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
    private readonly TextBox _nameBox = new() { Width = 240 };
    private readonly TextBox _kanaBox = new() { Width = 240 };
    private readonly TextBox _emailBox = new() { Width = 300 };
    private readonly TextBox _phoneBox = new() { Width = 180 };

    private readonly BindingList<CompanionRow> _companions = [];
    private readonly DataGridView _companionGrid = UiTheme.CreateGrid();
    private readonly Button _addCompanionButton = UiTheme.CreateButton("同行者を追加", 130);
    private readonly Button _removeCompanionButton = UiTheme.CreateButton("削除", 90);

    private readonly Button _saveButton = UiTheme.CreateButton("保存", 120);
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる", 120);

    /// <param name="reservationGroupId">編集対象の予約グループ（新規登録の場合は null）</param>
    public ReservationEditForm(IServiceProvider provider, Guid? reservationGroupId)
    {
        _provider = provider;
        _reservationService = provider.GetRequiredService<ReservationService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;
        _reservationGroupId = reservationGroupId;

        Text = reservationGroupId is null
            ? $"{UiTheme.AppName} - 来館予約 新規登録"
            : $"{UiTheme.AppName} - 来館予約 編集";
        ClientSize = new Size(880, 640);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        UiTheme.Apply(this);

        BuildLayout();

        Load += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        // --- 来館情報 ---
        var visitGroup = new GroupBox { Text = "来館情報", Location = new Point(16, 12), Size = new Size(844, 148) };
        AddRow(visitGroup, "来館先拠点", _siteBox, 0, required: true);
        AddRow(visitGroup, "来館日", _visitDatePicker, 1, required: true);

        var timeLabel = UiTheme.CreateLabel("来館時刻", required: true);
        timeLabel.Location = new Point(430, 26 + 32);
        timeLabel.Width = 90;
        _timeFromPicker.Location = new Point(526, 26 + 32);
        var timeSeparator = new Label { Text = "〜", Width = 24, Height = 26, TextAlign = ContentAlignment.MiddleCenter, Location = new Point(632, 26 + 32) };
        _timeToPicker.Location = new Point(660, 26 + 32);
        visitGroup.Controls.AddRange([timeLabel, _timeFromPicker, timeSeparator, _timeToPicker]);

        AddRow(visitGroup, "訪問先担当者", _hostBox, 2);
        AddRow(visitGroup, "来館目的", _purposeBox, 3);

        // --- 代表者 ---
        var representativeGroup = new GroupBox
        {
            Text = "代表者（QRコードはこの方に1件のみ発行されます）",
            Location = new Point(16, 168),
            Size = new Size(844, 180),
        };
        AddRow(representativeGroup, "会社名", _companyBox, 0);
        AddRow(representativeGroup, "氏名", _nameBox, 1, required: true);
        AddRow(representativeGroup, "フリガナ", _kanaBox, 2);
        AddRow(representativeGroup, "メールアドレス", _emailBox, 3, required: true);
        AddRow(representativeGroup, "電話番号", _phoneBox, 4);

        // --- 同行者 ---
        var companionGroup = new GroupBox
        {
            Text = "同行者（QRコードは発行されません。代表者のQRコードでグループ全員の入退館を受け付けます）",
            Location = new Point(16, 356),
            Size = new Size(844, 210),
        };

        _companionGrid.ReadOnly = false;
        _companionGrid.AllowUserToAddRows = false;
        _companionGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _companionGrid.Location = new Point(12, 24);
        _companionGrid.Size = new Size(680, 172);
        _companionGrid.Dock = DockStyle.None;
        _companionGrid.Columns.AddRange(
            UiTheme.TextColumn("会社名", nameof(CompanionRow.CompanyName), 200),
            UiTheme.TextColumn("氏名", nameof(CompanionRow.VisitorName), 160),
            UiTheme.TextColumn("フリガナ", nameof(CompanionRow.VisitorNameKana), 160),
            UiTheme.TextColumn("電話番号", nameof(CompanionRow.PhoneNumber), 140));
        _companionGrid.DataSource = _companions;

        _addCompanionButton.Location = new Point(702, 24);
        _removeCompanionButton.Location = new Point(702, 60);
        _addCompanionButton.Click += (_, _) => _companions.Add(new CompanionRow { CompanyName = _companyBox.Text });
        _removeCompanionButton.Click += (_, _) =>
        {
            if (_companionGrid.CurrentRow?.DataBoundItem is CompanionRow row)
            {
                _companions.Remove(row);
            }
        };

        companionGroup.Controls.AddRange([_companionGrid, _addCompanionButton, _removeCompanionButton]);

        // --- 操作ボタン ---
        _saveButton.Location = new Point(608, 584);
        _closeButton.Location = new Point(740, 584);
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();

        Controls.AddRange([visitGroup, representativeGroup, companionGroup, _saveButton, _closeButton]);
        CancelButton = _closeButton;
    }

    /// <summary>グループボックス内に「ラベル＋入力欄」の1行を配置する</summary>
    private static void AddRow(GroupBox group, string label, Control input, int rowIndex, bool required = false)
    {
        var y = 26 + (rowIndex * 32);

        var labelControl = UiTheme.CreateLabel(label, required);
        labelControl.Location = new Point(12, y);
        labelControl.Height = 26;

        input.Location = new Point(12 + UiTheme.LabelWidth + 8, y);

        group.Controls.Add(labelControl);
        group.Controls.Add(input);
    }

    /// <summary>選択肢の読み込みと、編集時の既存値の反映を行う</summary>
    private async Task InitializeAsync()
    {
        try
        {
            var masterDataService = _provider.GetRequiredService<MasterDataService>();

            // 拠点（受付ロールは自身の所属拠点に固定する）
            var sites = await masterDataService.GetSitesAsync().ConfigureAwait(true);
            foreach (var site in sites)
            {
                _siteBox.Items.Add(new ComboItem<int?>(site.SiteName, site.SiteId));
            }
            SelectSite(_ctx.SiteId);

            if (_ctx.IsReception)
            {
                _siteBox.Enabled = false;
            }
            // Entryプラン（1拠点構成）では拠点選択UIを非表示にする（設計書 6.11節）
            else if (sites.Count <= 1)
            {
                _siteBox.Enabled = false;
            }

            // 会社名マスタ（入力補助。設計書 6.9節）
            var companies = await masterDataService.GetCompaniesAsync().ConfigureAwait(true);
            _companyBox.Items.AddRange([.. companies.Select(c => c.CompanyName)]);

            _visitDatePicker.Value = DateTime.Today;
            _timeFromPicker.Value = DateTime.Today.AddHours(DateTime.Now.Hour + 1);
            _timeToPicker.Value = DateTime.Today.AddHours(DateTime.Now.Hour + 2);
            _timeToPicker.Checked = false;

            // 一般ロールは自分自身が訪問先担当者となる運用のため、新規登録時に自動入力する（変更は可能）
            if (_reservationGroupId is null && _ctx.IsGeneral)
            {
                _hostBox.Text = _ctx.DisplayName;
            }

            if (_reservationGroupId is { } groupId)
            {
                await LoadExistingAsync(groupId).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>編集対象の既存予約を画面へ反映する</summary>
    private async Task LoadExistingAsync(Guid groupId)
    {
        var group = await _reservationService.GetGroupAsync(groupId).ConfigureAwait(true);
        if (group is null)
        {
            UiTheme.ShowWarning(this, "対象の来館予約が見つかりません。");
            Close();
            return;
        }

        var representative = group.Representative;

        SelectSite(representative.SiteId);
        _visitDatePicker.Value = representative.VisitDate.ToDateTime(TimeOnly.MinValue);
        _timeFromPicker.Value = DateTime.Today.Add(representative.VisitTimeFrom.ToTimeSpan());
        if (representative.VisitTimeTo is { } to)
        {
            _timeToPicker.Value = DateTime.Today.Add(to.ToTimeSpan());
            _timeToPicker.Checked = true;
        }

        _hostBox.Text = representative.HostEmployeeName;
        _purposeBox.Text = representative.PurposeOfVisit;
        _companyBox.Text = representative.CompanyName;
        _nameBox.Text = representative.VisitorName;
        _kanaBox.Text = representative.VisitorNameKana;
        _emailBox.Text = representative.VisitorEmail;
        _phoneBox.Text = representative.PhoneNumber;

        foreach (var companion in group.Companions)
        {
            _companions.Add(new CompanionRow
            {
                VisitorId = companion.VisitorId,
                CompanyName = companion.CompanyName,
                VisitorName = companion.VisitorName,
                VisitorNameKana = companion.VisitorNameKana,
                PhoneNumber = companion.PhoneNumber,
            });
        }

        // 当日受付分はメールアドレスが任意のため、必須表示を外す
        if (representative.ReservationType == ReservationType.WalkIn)
        {
            Text += "（当日受付）";
        }
    }

    /// <summary>拠点コンボの選択を指定IDに合わせる</summary>
    private void SelectSite(int siteId)
    {
        for (var i = 0; i < _siteBox.Items.Count; i++)
        {
            if (_siteBox.Items[i] is ComboItem<int?> item && item.Value == siteId)
            {
                _siteBox.SelectedIndex = i;
                return;
            }
        }

        if (_siteBox.Items.Count > 0)
        {
            _siteBox.SelectedIndex = 0;
        }
    }

    /// <summary>入力内容を保存する</summary>
    private async Task SaveAsync()
    {
        // 同行者グリッドの編集途中の値を確定させる
        _companionGrid.EndEdit();

        var siteId = (_siteBox.SelectedItem as ComboItem<int?>)?.Value;
        if (siteId is null)
        {
            UiTheme.ShowWarning(this, "来館先の拠点を選択してください。");
            return;
        }

        var input = new ReservationInput
        {
            SiteId = siteId.Value,
            VisitDate = DateOnly.FromDateTime(_visitDatePicker.Value),
            VisitTimeFrom = TimeOnly.FromDateTime(_timeFromPicker.Value),
            VisitTimeTo = _timeToPicker.Checked ? TimeOnly.FromDateTime(_timeToPicker.Value) : null,
            HostEmployeeName = _hostBox.Text,
            PurposeOfVisit = _purposeBox.Text,
            ReservationType = ReservationType.Advance,
            Representative = new ReservationMemberInput
            {
                CompanyName = _companyBox.Text,
                VisitorName = _nameBox.Text,
                VisitorNameKana = _kanaBox.Text,
                VisitorEmail = _emailBox.Text,
                PhoneNumber = _phoneBox.Text,
            },
            Companions = [.. _companions
                .Where(c => !string.IsNullOrWhiteSpace(c.VisitorName))
                .Select(c => new ReservationMemberInput
                {
                    VisitorId = c.VisitorId,
                    CompanyName = c.CompanyName,
                    VisitorName = c.VisitorName ?? string.Empty,
                    VisitorNameKana = c.VisitorNameKana,
                    PhoneNumber = c.PhoneNumber,
                })],
        };

        try
        {
            _saveButton.Enabled = false;

            if (_reservationGroupId is { } groupId)
            {
                await UiTheme.RunWithWaitCursorAsync(this, async () =>
                    await _reservationService.UpdateAsync(groupId, input, _ctx).ConfigureAwait(true))
                    .ConfigureAwait(true);

                UiTheme.ShowInfo(this, "来館予約を更新しました。");
            }
            else
            {
                ReservationResult? result = null;
                await UiTheme.RunWithWaitCursorAsync(this, async () =>
                    result = await _reservationService.CreateAsync(input, _ctx).ConfigureAwait(true))
                    .ConfigureAwait(true);

                ShowCreateResult(result!);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
        finally
        {
            _saveButton.Enabled = true;
        }
    }

    /// <summary>登録結果（QR発行とメール送信の結果）を表示する</summary>
    private void ShowCreateResult(ReservationResult result)
    {
        var mailSummary = result.MailResults.Count == 0
            ? "メールの送信先が設定されていないため、送信は行われませんでした。"
            : string.Join(Environment.NewLine, result.MailResults.Select(r =>
                $"・{HistoryService.ToText(r.RecipientType)}（{r.Address}）: {r.ToDisplayText()}"));

        // テストモード（デモ版・Debugビルド）では実送信していないことを明示する
        if (result.MailResults.Any(r => r.IsTest))
        {
            mailSummary += Environment.NewLine
                        + "※ テストモードのため外部へのメール送信は行っていません（送信履歴には記録されます）。";
        }

        var message = $"来館予約を登録し、QRコードを発行しました（{result.Group.AllMembers.Count()}名）。"
                    + Environment.NewLine + Environment.NewLine
                    + "【メール送信結果】" + Environment.NewLine + mailSummary;

        UiTheme.ShowInfo(this, message);

        using var preview = new QrCodePreviewForm(_provider, result.Group, result.QrCodePng);
        preview.ShowDialog(this);
    }

    /// <summary>
    /// 同行者グリッドの1行分。
    /// VisitorId は編集時に既存行を特定するために保持する（新規行は null）。
    /// </summary>
    private sealed class CompanionRow
    {
        public Guid? VisitorId { get; init; }

        public string? CompanyName { get; set; }

        public string? VisitorName { get; set; }

        public string? VisitorNameKana { get; set; }

        public string? PhoneNumber { get; set; }
    }
}
