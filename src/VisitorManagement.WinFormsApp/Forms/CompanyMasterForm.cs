using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 会社名マスタ管理画面（設計書 8.1節 画面No.4、6.9節）。
/// 来館予約入力を補助する会社名マスタの登録・編集・削除を行う。管理者ロール専用。
/// 変更内容は監査ログ（AuditLog）へ記録される（設計書 11.5節）。
/// </summary>
public sealed class CompanyMasterForm : Form
{
    private readonly MasterDataService _masterDataService;
    private readonly OperatorContext _ctx;

    private readonly DataGridView _grid = UiTheme.CreateGrid();
    private readonly BindingSource _bindingSource = [];

    private readonly TextBox _nameBox = new() { Width = 300 };
    private readonly TextBox _addressBox = new() { Width = 400 };
    private readonly TextBox _telBox = new() { Width = 180 };

    private readonly Button _newButton = UiTheme.CreateButton("新規");
    private readonly Button _saveButton = UiTheme.CreateButton("保存");
    private readonly Button _deleteButton = UiTheme.CreateButton("削除");
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");

    private int? _editingCompanyId;

    public CompanyMasterForm(IServiceProvider provider)
    {
        _masterDataService = provider.GetRequiredService<MasterDataService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - 会社名マスタ管理";
        ClientSize = new Size(900, 560);
        MinimumSize = new Size(760, 480);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        Load += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        _grid.Columns.AddRange(
            UiTheme.TextColumn("会社名", nameof(Company.CompanyName), 260),
            UiTheme.TextColumn("住所", nameof(Company.Address), 320, fill: true),
            UiTheme.TextColumn("電話番号", nameof(Company.Tel), 150));
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => LoadSelection();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0) };
        gridPanel.Controls.Add(_grid);

        var editGroup = new GroupBox
        {
            Dock = DockStyle.Bottom,
            Height = 180,
            Text = "登録・編集",
            Padding = new Padding(12),
        };

        AddRow(editGroup, "会社名", _nameBox, 0, required: true);
        AddRow(editGroup, "住所", _addressBox, 1);
        AddRow(editGroup, "電話番号", _telBox, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.AddRange([_newButton, _saveButton, _deleteButton, _closeButton]);

        _newButton.Click += (_, _) => ClearInput();
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _deleteButton.Click += async (_, _) => await DeleteAsync().ConfigureAwait(true);
        _closeButton.Click += (_, _) => Close();

        Controls.Add(gridPanel);
        Controls.Add(buttonPanel);
        Controls.Add(editGroup);

        CancelButton = _closeButton;
    }

    /// <summary>グループボックス内に「ラベル＋入力欄」の1行を配置する</summary>
    private static void AddRow(GroupBox group, string label, Control input, int rowIndex, bool required = false)
    {
        var y = 26 + (rowIndex * 34);

        var labelControl = UiTheme.CreateLabel(label, required);
        labelControl.Location = new Point(12, y);
        labelControl.Height = 26;

        input.Location = new Point(12 + UiTheme.LabelWidth + 8, y);

        group.Controls.Add(labelControl);
        group.Controls.Add(input);
    }

    /// <summary>一覧を読み込む</summary>
    private async Task LoadAsync()
    {
        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var companies = await _masterDataService.GetCompaniesAsync().ConfigureAwait(true);
                _bindingSource.DataSource = companies.ToList();
            }).ConfigureAwait(true);

            ClearInput();
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択行を入力欄へ反映する</summary>
    private void LoadSelection()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Company company)
        {
            return;
        }

        _editingCompanyId = company.CompanyId;
        _nameBox.Text = company.CompanyName;
        _addressBox.Text = company.Address;
        _telBox.Text = company.Tel;
        _deleteButton.Enabled = true;
    }

    /// <summary>入力欄をクリアして新規登録状態にする</summary>
    private void ClearInput()
    {
        _editingCompanyId = null;
        _nameBox.Clear();
        _addressBox.Clear();
        _telBox.Clear();
        _deleteButton.Enabled = false;
        _nameBox.Focus();
    }

    /// <summary>入力内容を保存する</summary>
    private async Task SaveAsync()
    {
        var company = new Company
        {
            CompanyId = _editingCompanyId ?? 0,
            CompanyName = _nameBox.Text.Trim(),
            Address = NullIfBlank(_addressBox.Text),
            Tel = NullIfBlank(_telBox.Text),
        };

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                if (_editingCompanyId is null)
                {
                    await _masterDataService.CreateCompanyAsync(company, _ctx).ConfigureAwait(true);
                }
                else
                {
                    await _masterDataService.UpdateCompanyAsync(company, _ctx).ConfigureAwait(true);
                }
            }).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の会社を削除する</summary>
    private async Task DeleteAsync()
    {
        if (_editingCompanyId is not { } companyId)
        {
            return;
        }

        if (!UiTheme.Confirm(this, $"「{_nameBox.Text}」を削除します。よろしいですか？"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _masterDataService.DeleteCompanyAsync(companyId, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>空白のみの入力は null として扱う</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
