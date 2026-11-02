using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Security;
using VisitorManagement.Core.Settings;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// メイン画面（設計書 8.2節）。
/// メニューから各機能をモーダルのサブフォームとして開く。
/// ログイン中の権限ロールに応じてメニュー項目の表示・活性状態を制御する（設計書 11.6節）。
/// </summary>
public sealed class MainForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly OperatorContext _operatorContext;

    private readonly ToolStripMenuItem _reservationMenu = new("予約管理(&R)");
    private readonly ToolStripMenuItem _manualCheckOutMenu = new("手動退館処理(&M)");
    private readonly ToolStripMenuItem _historyMenu = new("履歴照会(&H)");
    private readonly ToolStripMenuItem _companyMasterMenu = new("会社名マスタ管理(&C)");
    private readonly ToolStripMenuItem _siteMasterMenu = new("拠点マスタ管理(&S)");
    private readonly ToolStripMenuItem _operatorMasterMenu = new("操作ユーザー管理(&U)");
    private readonly ToolStripMenuItem _systemSettingsMenu = new("システム設定確認(&T)");
    private readonly ToolStripMenuItem _demoResetMenu = new("サンプルデータ初期化(&D)");

    public MainForm(IServiceProvider provider)
    {
        _provider = provider;
        _operatorContext = provider.GetRequiredService<AppSession>().Current;

        Text = UiTheme.AppName;
        ClientSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        UiTheme.Apply(this);

        BuildLayout();
        ApplyPermissions();
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        // --- メニュー ---
        var menu = new MenuStrip { Font = UiTheme.BaseFont };

        var operationMenu = new ToolStripMenuItem("業務(&O)");
        operationMenu.DropDownItems.AddRange([_reservationMenu, _manualCheckOutMenu, _historyMenu]);

        var masterMenu = new ToolStripMenuItem("マスタ(&M)");
        masterMenu.DropDownItems.AddRange([_companyMasterMenu, _siteMasterMenu, _operatorMasterMenu]);

        var systemMenu = new ToolStripMenuItem("システム(&S)");
        var logoutMenu = new ToolStripMenuItem("ログアウトして終了(&L)");
        logoutMenu.Click += (_, _) => Close();
        systemMenu.DropDownItems.AddRange([
            _systemSettingsMenu, _demoResetMenu, new ToolStripSeparator(), logoutMenu]);

        menu.Items.AddRange([operationMenu, masterMenu, systemMenu]);
        MainMenuStrip = menu;

        _reservationMenu.Click += (_, _) => ShowDialog<ReservationListForm>();
        _manualCheckOutMenu.Click += (_, _) => ShowDialog<ManualCheckOutForm>();
        _historyMenu.Click += (_, _) => ShowDialog<HistoryForm>();
        _companyMasterMenu.Click += (_, _) => ShowDialog<CompanyMasterForm>();
        _siteMasterMenu.Click += (_, _) => ShowDialog<SiteMasterForm>();
        _operatorMasterMenu.Click += (_, _) => ShowDialog<OperatorMasterForm>();
        _systemSettingsMenu.Click += (_, _) => ShowDialog<SystemSettingsForm>();
        _demoResetMenu.Click += async (_, _) => await ResetDemoDataAsync().ConfigureAwait(true);

        // --- 中央のショートカットボタン ---
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(40, 30, 40, 30),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.White,
        };

        var heading = new Label
        {
            Text = UiTheme.AppName,
            Font = new Font("Meiryo UI", 18F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(4, 0, 4, 4),
        };

        var operatorInfo = new Label
        {
            Text = $"ログイン中: {_operatorContext.DisplayName}（{HistoryService.ToText(_operatorContext.RoleType)}）"
                 + $"　所属拠点: {_operatorContext.SiteName}",
            AutoSize = true,
            ForeColor = Color.FromArgb(80, 80, 80),
            Margin = new Padding(6, 0, 4, 20),
        };

        panel.Controls.Add(heading);
        panel.SetFlowBreak(heading, true);
        panel.Controls.Add(operatorInfo);
        panel.SetFlowBreak(operatorInfo, true);

        foreach (var (text, menuItem) in new (string, ToolStripMenuItem)[]
        {
            ("来館予約管理", _reservationMenu),
            ("手動退館処理", _manualCheckOutMenu),
            ("履歴照会", _historyMenu),
            ("会社名マスタ管理", _companyMasterMenu),
            ("拠点マスタ管理", _siteMasterMenu),
            ("操作ユーザー管理", _operatorMasterMenu),
            ("システム設定確認", _systemSettingsMenu),
        })
        {
            var button = new Button
            {
                Text = text,
                Width = 190,
                Height = 70,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(8),
                Font = new Font("Meiryo UI", 10F),
            };

            // メニュー項目と活性状態を連動させ、権限制御を1か所に集約する
            button.Click += (_, _) => menuItem.PerformClick();
            button.DataBindings.Add(nameof(Button.Enabled), menuItem, nameof(ToolStripMenuItem.Enabled), false,
                DataSourceUpdateMode.Never);
            button.EnabledChanged += (s, _) =>
            {
                if (s is Button b)
                {
                    b.Visible = b.Enabled;
                }
            };

            panel.Controls.Add(button);
        }

        // --- ステータスバー ---
        var statusStrip = new StatusStrip();
        var providerLabel = new ToolStripStatusLabel(
            $"DB方式: {_provider.GetRequiredService<Core.Abstractions.IDbConnectionFactory>().ProviderName}");
        var demoLabel = new ToolStripStatusLabel(
            _provider.GetRequiredService<IOptions<DemoSettings>>().Value.Enabled ? "デモモード" : string.Empty)
        {
            ForeColor = Color.FromArgb(180, 90, 0),
        };
        statusStrip.Items.AddRange([providerLabel, demoLabel]);

        Controls.Add(panel);
        Controls.Add(statusStrip);
        Controls.Add(menu);
    }

    /// <summary>権限ロールに応じてメニューの活性状態を設定する（設計書 11.6節）</summary>
    private void ApplyPermissions()
    {
        var ctx = _operatorContext;

        // 一般ロールは予約・履歴の閲覧のみ可能（画面内で登録・編集ボタンを非活性にする）
        _reservationMenu.Enabled = true;
        _historyMenu.Enabled = true;

        _manualCheckOutMenu.Enabled = PermissionPolicy.CanManualCheckOut(ctx);
        _companyMasterMenu.Enabled = PermissionPolicy.CanManageCompanies(ctx);
        _siteMasterMenu.Enabled = PermissionPolicy.CanManageSites(ctx);
        _operatorMasterMenu.Enabled = PermissionPolicy.CanManageOperators(ctx);
        _systemSettingsMenu.Enabled = PermissionPolicy.CanViewSystemSettings(ctx);

        // サンプルデータ初期化はデモ版かつ管理者ロールのときのみ（設計書 19.3節）
        var demoSettings = _provider.GetRequiredService<IOptions<DemoSettings>>().Value;
        var demoAvailable = demoSettings.Enabled
                            && demoSettings.AllowResetFromAdminMenu
                            && PermissionPolicy.CanResetDemoData(ctx);
        _demoResetMenu.Visible = demoAvailable;
        _demoResetMenu.Enabled = demoAvailable;
    }

    /// <summary>サブフォームをモーダルで開く</summary>
    private void ShowDialog<TForm>() where TForm : Form
    {
        try
        {
            using var form = (TForm)Activator.CreateInstance(typeof(TForm), _provider)!;
            form.ShowDialog(this);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>デモ用サンプルデータを初期状態へリセットする（設計書 19.3節）</summary>
    private async Task ResetDemoDataAsync()
    {
        if (!UiTheme.Confirm(this,
            "デモ用データベースの内容をすべて削除し、サンプルデータを再投入します。よろしいですか？"))
        {
            return;
        }

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var seeder = _provider.GetRequiredService<DemoDataSeeder>();
                await seeder.ResetAsync(_operatorContext).ConfigureAwait(true);
            }).ConfigureAwait(true);

            UiTheme.ShowInfo(this, "サンプルデータを初期状態へリセットしました。");
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }
}
