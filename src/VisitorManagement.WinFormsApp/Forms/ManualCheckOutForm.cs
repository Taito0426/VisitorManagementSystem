using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 手動退館処理画面（設計書 6.4.1節。グループはぐれ対応）。
/// 代表者と同行者が別々のタイミングで退館する場合に、対象者を個人単位で選択して
/// 退館ステータスを更新する。氏名・来館日での検索にも対応する。
/// 本機能は退館処理のみを対象とし、入館側の個別対応は設けない。
/// 受付・管理者ロールのみ利用可能。
/// </summary>
public sealed class ManualCheckOutForm : Form
{
    private readonly AccessService _accessService;
    private readonly IServiceProvider _provider;
    private readonly OperatorContext _ctx;

    private readonly TextBox _visitorNameBox = new() { Width = 160 };
    private readonly DateTimePicker _visitDatePicker = new() { Width = 130, Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly ComboBox _siteBox = new() { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _noteBox = new() { Width = 420 };

    private readonly DataGridView _grid = UiTheme.CreateGrid();
    private readonly BindingSource _bindingSource = [];

    private readonly Button _searchButton = UiTheme.CreateButton("検索");
    private readonly Button _checkOutButton = UiTheme.CreateButton("退館処理", 130);
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");
    private readonly Label _countLabel = new() { AutoSize = true, Margin = new Padding(12, 10, 4, 4) };

    public ManualCheckOutForm(IServiceProvider provider)
    {
        _provider = provider;
        _accessService = provider.GetRequiredService<AccessService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - 手動退館処理";
        ClientSize = new Size(1020, 560);
        MinimumSize = new Size(880, 480);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        Load += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        var description = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = "入館中の来館者を個人単位で退館処理します（代表者と同行者が別々に退館する場合などに使用します）。",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 0, 0),
            ForeColor = Color.FromArgb(80, 80, 80),
        };

        var conditionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(12, 10, 12, 6),
        };

        AddCondition(conditionPanel, "氏名", _visitorNameBox);
        AddCondition(conditionPanel, "来館日", _visitDatePicker);
        AddCondition(conditionPanel, "拠点", _siteBox);
        conditionPanel.Controls.Add(_searchButton);

        _grid.Columns.AddRange(
            UiTheme.TextColumn("来館日", nameof(TargetRow.VisitDateText), 100),
            UiTheme.TextColumn("拠点", nameof(TargetRow.SiteName), 120),
            UiTheme.TextColumn("氏名", nameof(TargetRow.VisitorName), 150),
            UiTheme.TextColumn("会社名", nameof(TargetRow.CompanyName), 200),
            UiTheme.TextColumn("区分", nameof(TargetRow.RoleText), 80),
            UiTheme.TextColumn("入館時刻", nameof(TargetRow.CheckInText), 150),
            UiTheme.TextColumn("ステータス", nameof(TargetRow.StatusText), 100, fill: true));
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => _checkOutButton.Enabled = SelectedRow is not null;

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 0) };
        gridPanel.Controls.Add(_grid);

        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 92,
            Padding = new Padding(12, 8, 12, 8),
        };

        var noteLabel = new Label
        {
            Text = "備考",
            Width = 60,
            Height = 26,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(4, 6, 2, 4),
        };
        _noteBox.Margin = new Padding(2, 4, 10, 4);
        _noteBox.PlaceholderText = "未入力の場合は「グループはぐれのため手動退館処理」として記録されます";

        bottomPanel.Controls.Add(noteLabel);
        bottomPanel.Controls.Add(_noteBox);
        bottomPanel.SetFlowBreak(_noteBox, true);
        bottomPanel.Controls.AddRange([_countLabel, _checkOutButton, _closeButton]);

        _checkOutButton.Enabled = false;
        _searchButton.Click += async (_, _) => await SearchAsync().ConfigureAwait(true);
        _checkOutButton.Click += async (_, _) => await CheckOutAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();

        Controls.Add(gridPanel);
        Controls.Add(bottomPanel);
        Controls.Add(conditionPanel);
        Controls.Add(description);

        AcceptButton = _searchButton;
        CancelButton = _closeButton;
    }

    /// <summary>検索条件の「ラベル＋入力欄」を1組追加する</summary>
    private static void AddCondition(Control parent, string labelText, Control input, int labelWidth = 70)
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
            _siteBox.Enabled = !_ctx.IsReception;

            _visitDatePicker.Value = DateTime.Today;
            _visitDatePicker.Checked = true;

            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>入館中の来館者を検索する</summary>
    private async Task SearchAsync()
    {
        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var targets = await _accessService.SearchManualCheckOutTargetsAsync(
                    _visitorNameBox.Text,
                    _visitDatePicker.Checked ? DateOnly.FromDateTime(_visitDatePicker.Value) : null,
                    (_siteBox.SelectedItem as ComboItem<int?>)?.Value,
                    _ctx).ConfigureAwait(true);

                _bindingSource.DataSource = targets.Select(t => new TargetRow(t)).ToList();
                _countLabel.Text = $"入館中 {targets.Count} 名";
            }).ConfigureAwait(true);

            _checkOutButton.Enabled = SelectedRow is not null;
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の来館者を退館処理する</summary>
    private async Task CheckOutAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        if (!UiTheme.Confirm(this,
            $"「{row.VisitorName}」様を退館済みにします。よろしいですか？"
            + Environment.NewLine + "※ この操作は選択した1名のみに適用されます（グループ全員には適用されません）。"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _accessService.ManualCheckOutAsync(row.Source.VisitorId, _noteBox.Text, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            UiTheme.ShowInfo(this, $"「{row.VisitorName}」様の退館処理が完了しました。");
            _noteBox.Clear();
            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の行</summary>
    private TargetRow? SelectedRow => _grid.CurrentRow?.DataBoundItem as TargetRow;

    /// <summary>手動退館対象の表示行</summary>
    private sealed class TargetRow(ManualCheckOutTarget source)
    {
        public ManualCheckOutTarget Source { get; } = source;

        public string VisitDateText => Source.VisitDate.ToString("yyyy/MM/dd");

        public string? SiteName => Source.SiteName;

        public string VisitorName => Source.VisitorName;

        public string? CompanyName => Source.CompanyName;

        public string RoleText => Source.IsRepresentative ? "代表者" : "同行者";

        public string CheckInText => Source.CheckInDateTime is { } dt ? HistoryService.ToLocalText(dt) : "-";

        public string StatusText => HistoryService.ToText(Source.Status);
    }
}
