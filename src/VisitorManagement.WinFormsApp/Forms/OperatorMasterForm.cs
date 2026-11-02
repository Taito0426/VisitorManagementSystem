using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 操作ユーザー管理画面（設計書 8.1節 画面No.6、6.10節）。
/// 操作ユーザーの登録・編集・無効化と、メールアドレス・所属拠点・権限ロールの設定を行う。管理者ロール専用。
/// 権限ロールと所属拠点は登録時必須であり、未設定状態は発生しない（設計書 17章 確認事項No.4）。
/// 拠点間の異動は本画面で所属拠点を変更して対応する（個別の拠点切替UIは設けない）。
/// </summary>
public sealed class OperatorMasterForm : Form
{
    private readonly OperatorService _operatorService;
    private readonly MasterDataService _masterDataService;
    private readonly OperatorContext _ctx;

    private readonly DataGridView _grid = UiTheme.CreateGrid();
    private readonly BindingSource _bindingSource = [];

    private readonly TextBox _loginIdBox = new() { Width = 240 };
    private readonly TextBox _displayNameBox = new() { Width = 240 };
    private readonly TextBox _emailBox = new() { Width = 320 };
    private readonly ComboBox _roleBox = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _siteBox = new() { Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _passwordBox = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly CheckBox _isActiveBox = new() { Text = "有効", AutoSize = true };

    private readonly Button _newButton = UiTheme.CreateButton("新規");
    private readonly Button _saveButton = UiTheme.CreateButton("保存");
    private readonly Button _passwordButton = UiTheme.CreateButton("パスワード変更", 140);
    private readonly Button _deactivateButton = UiTheme.CreateButton("無効化", 110);
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");

    private int? _editingOperatorId;

    public OperatorMasterForm(IServiceProvider provider)
    {
        _operatorService = provider.GetRequiredService<OperatorService>();
        _masterDataService = provider.GetRequiredService<MasterDataService>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - 操作ユーザー管理";
        ClientSize = new Size(980, 620);
        MinimumSize = new Size(860, 560);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        Load += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        _grid.Columns.AddRange(
            UiTheme.TextColumn("ログインID", nameof(OperatorRow.LoginId), 150),
            UiTheme.TextColumn("表示名", nameof(OperatorRow.DisplayName), 150),
            UiTheme.TextColumn("メールアドレス", nameof(OperatorRow.Email), 240, fill: true),
            UiTheme.TextColumn("権限ロール", nameof(OperatorRow.RoleText), 100),
            UiTheme.TextColumn("所属拠点", nameof(OperatorRow.SiteName), 140),
            UiTheme.TextColumn("状態", nameof(OperatorRow.StatusText), 80));
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => LoadSelection();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0) };
        gridPanel.Controls.Add(_grid);

        var editGroup = new GroupBox
        {
            Dock = DockStyle.Bottom,
            Height = 250,
            Text = "登録・編集",
            Padding = new Padding(12),
        };

        AddRow(editGroup, "ログインID", _loginIdBox, 0, required: true);
        AddRow(editGroup, "表示名", _displayNameBox, 1, required: true);
        AddRow(editGroup, "メールアドレス", _emailBox, 2, required: true);
        AddRow(editGroup, "権限ロール", _roleBox, 3, required: true);
        AddRow(editGroup, "所属拠点", _siteBox, 4, required: true);
        AddRow(editGroup, "パスワード", _passwordBox, 5, required: true);

        var passwordNote = new Label
        {
            Text = "新規登録時のみ入力（8文字以上）。既存ユーザーは「パスワード変更」から更新します。",
            AutoSize = true,
            ForeColor = Color.FromArgb(110, 110, 110),
            Location = new Point(12 + UiTheme.LabelWidth + 260, 26 + (5 * 34) + 4),
        };

        _isActiveBox.Location = new Point(12 + UiTheme.LabelWidth + 8, 26 + (6 * 34));
        editGroup.Controls.AddRange([passwordNote, _isActiveBox]);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.AddRange([_newButton, _saveButton, _passwordButton, _deactivateButton, _closeButton]);

        _newButton.Click += (_, _) => ClearInput();
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
        _passwordButton.Click += async (_, _) => await ChangePasswordAsync().ConfigureAwait(true);
        _deactivateButton.Click += async (_, _) => await DeactivateAsync().ConfigureAwait(true);
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

    /// <summary>一覧と選択肢を読み込む</summary>
    private async Task LoadAsync()
    {
        try
        {
            if (_roleBox.Items.Count == 0)
            {
                foreach (var role in new[] { RoleType.Administrator, RoleType.Reception, RoleType.General })
                {
                    _roleBox.Items.Add(new ComboItem<RoleType>(HistoryService.ToText(role), role));
                }
            }

            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var sites = await _masterDataService.GetSitesAsync().ConfigureAwait(true);
                _siteBox.Items.Clear();
                foreach (var site in sites)
                {
                    _siteBox.Items.Add(new ComboItem<int>(site.SiteName, site.SiteId));
                }

                var operators = await _operatorService.GetAllAsync(_ctx).ConfigureAwait(true);
                _bindingSource.DataSource = operators.Select(o => new OperatorRow(o)).ToList();
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
        if (_grid.CurrentRow?.DataBoundItem is not OperatorRow row)
        {
            return;
        }

        var op = row.Source;
        _editingOperatorId = op.OperatorId;
        _loginIdBox.Text = op.LoginId;
        _displayNameBox.Text = op.DisplayName;
        _emailBox.Text = op.Email;
        _isActiveBox.Checked = op.IsActive;
        SelectCombo(_roleBox, op.RoleType);
        SelectCombo(_siteBox, op.SiteId);

        // 既存ユーザーのパスワードは専用ボタンから変更する
        _passwordBox.Clear();
        _passwordBox.Enabled = false;
        _passwordButton.Enabled = true;
        _deactivateButton.Enabled = op.IsActive && op.OperatorId != _ctx.OperatorId;
    }

    /// <summary>入力欄をクリアして新規登録状態にする</summary>
    private void ClearInput()
    {
        _editingOperatorId = null;
        _loginIdBox.Clear();
        _displayNameBox.Clear();
        _emailBox.Clear();
        _passwordBox.Clear();
        _passwordBox.Enabled = true;
        _isActiveBox.Checked = true;
        _roleBox.SelectedIndex = _roleBox.Items.Count > 0 ? _roleBox.Items.Count - 1 : -1;
        _siteBox.SelectedIndex = _siteBox.Items.Count > 0 ? 0 : -1;
        _passwordButton.Enabled = false;
        _deactivateButton.Enabled = false;
        _loginIdBox.Focus();
    }

    /// <summary>コンボボックスの選択を指定値に合わせる</summary>
    private static void SelectCombo<T>(ComboBox comboBox, T value) where T : notnull
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboItem<T> item && item.Value.Equals(value))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>入力内容を保存する</summary>
    private async Task SaveAsync()
    {
        var roleType = (_roleBox.SelectedItem as ComboItem<RoleType>)?.Value;
        var siteId = (_siteBox.SelectedItem as ComboItem<int>)?.Value;

        if (roleType is null)
        {
            UiTheme.ShowWarning(this, "権限ロールを選択してください。");
            return;
        }
        if (siteId is null)
        {
            UiTheme.ShowWarning(this, "所属拠点を選択してください。");
            return;
        }

        var op = new Operator
        {
            OperatorId = _editingOperatorId ?? 0,
            LoginId = _loginIdBox.Text.Trim(),
            DisplayName = _displayNameBox.Text.Trim(),
            Email = _emailBox.Text.Trim(),
            RoleType = roleType.Value,
            SiteId = siteId.Value,
            IsActive = _isActiveBox.Checked,
        };

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                if (_editingOperatorId is null)
                {
                    await _operatorService.CreateAsync(op, _passwordBox.Text, _ctx).ConfigureAwait(true);
                }
                else
                {
                    await _operatorService.UpdateAsync(op, _ctx).ConfigureAwait(true);
                }
            }).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の操作ユーザーのパスワードを変更する</summary>
    private async Task ChangePasswordAsync()
    {
        if (_editingOperatorId is not { } operatorId)
        {
            return;
        }

        using var dialog = new PasswordInputForm(_loginIdBox.Text);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _operatorService.ChangePasswordAsync(operatorId, dialog.Password, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            UiTheme.ShowInfo(this, "パスワードを変更しました。");
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>選択中の操作ユーザーを無効化する</summary>
    private async Task DeactivateAsync()
    {
        if (_editingOperatorId is not { } operatorId)
        {
            return;
        }

        if (!UiTheme.Confirm(this, $"操作ユーザー「{_displayNameBox.Text}」を無効化します。よろしいですか？"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                await _operatorService.DeactivateAsync(operatorId, _ctx).ConfigureAwait(true))
                .ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>操作ユーザー一覧の表示行</summary>
    private sealed class OperatorRow(Operator source)
    {
        public Operator Source { get; } = source;

        public string LoginId => Source.LoginId;

        public string DisplayName => Source.DisplayName;

        public string Email => Source.Email;

        public string RoleText => HistoryService.ToText(Source.RoleType);

        public string? SiteName => Source.SiteName;

        public string StatusText => Source.IsActive ? "有効" : "無効";
    }
}

/// <summary>
/// パスワード変更用の入力ダイアログ。
/// </summary>
internal sealed class PasswordInputForm : Form
{
    private readonly TextBox _passwordBox = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly TextBox _confirmBox = new() { Width = 240, UseSystemPasswordChar = true };

    /// <summary>入力されたパスワード</summary>
    public string Password => _passwordBox.Text;

    public PasswordInputForm(string loginId)
    {
        Text = $"{UiTheme.AppName} - パスワード変更";
        ClientSize = new Size(440, 200);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        UiTheme.Apply(this);

        var heading = new Label
        {
            Text = $"「{loginId}」のパスワードを変更します。",
            AutoSize = true,
            Location = new Point(20, 20),
        };

        var passwordLabel = UiTheme.CreateLabel("新しいパスワード", required: true);
        passwordLabel.Location = new Point(20, 58);
        _passwordBox.Location = new Point(20 + UiTheme.LabelWidth + 20, 58);

        var confirmLabel = UiTheme.CreateLabel("確認用", required: true);
        confirmLabel.Location = new Point(20, 94);
        _confirmBox.Location = new Point(20 + UiTheme.LabelWidth + 20, 94);

        var okButton = UiTheme.CreateButton("変更", 110);
        okButton.Location = new Point(196, 146);
        var cancelButton = UiTheme.CreateButton("キャンセル", 110);
        cancelButton.Location = new Point(316, 146);

        okButton.Click += (_, _) =>
        {
            if (_passwordBox.Text.Length < 8)
            {
                UiTheme.ShowWarning(this, "パスワードは8文字以上で入力してください。");
                return;
            }
            if (_passwordBox.Text != _confirmBox.Text)
            {
                UiTheme.ShowWarning(this, "パスワードと確認用パスワードが一致しません。");
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };
        cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([heading, passwordLabel, _passwordBox, confirmLabel, _confirmBox, okButton, cancelButton]);
        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}
