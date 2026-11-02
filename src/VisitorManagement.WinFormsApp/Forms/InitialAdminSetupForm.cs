using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 初期セットアップ画面（設計書 13.1節 ⑤⑥）。
/// 操作ユーザーが1件も登録されていない初回起動時のみ表示し、
/// 最初の拠点と管理者アカウントを登録する。
/// 以降の拠点・操作ユーザーの追加は、それぞれの管理画面から行う。
/// </summary>
public sealed class InitialAdminSetupForm : Form
{
    private readonly IServiceProvider _provider;

    private readonly TextBox _siteNameBox = new() { Width = 280 };
    private readonly TextBox _siteAddressBox = new() { Width = 280 };
    private readonly TextBox _loginIdBox = new() { Width = 280 };
    private readonly TextBox _displayNameBox = new() { Width = 280 };
    private readonly TextBox _emailBox = new() { Width = 280 };
    private readonly TextBox _passwordBox = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly TextBox _passwordConfirmBox = new() { Width = 280, UseSystemPasswordChar = true };
    private readonly Button _registerButton = UiTheme.CreateButton("登録して開始", 140);

    public InitialAdminSetupForm(IServiceProvider provider)
    {
        _provider = provider;

        Text = $"{UiTheme.AppName} - 初期セットアップ";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 470);
        StartPosition = FormStartPosition.CenterScreen;
        UiTheme.Apply(this);

        BuildLayout();

        _siteNameBox.Text = "本社";
        _registerButton.Click += async (_, _) => await RegisterAsync().ConfigureAwait(true);
        AcceptButton = _registerButton;
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        var heading = new Label
        {
            Text = "初期セットアップ",
            Font = UiTheme.HeadingFont,
            AutoSize = true,
            Location = new Point(24, 20),
        };

        var description = new Label
        {
            Text = "操作ユーザーがまだ登録されていません。"
                 + Environment.NewLine
                 + "最初の拠点と、システムを管理する管理者アカウントを登録してください。",
            AutoSize = false,
            Size = new Size(510, 44),
            Location = new Point(24, 52),
            ForeColor = Color.FromArgb(80, 80, 80),
        };

        var siteGroup = new GroupBox
        {
            Text = "拠点",
            Location = new Point(24, 104),
            Size = new Size(510, 108),
        };
        AddRow(siteGroup, "拠点名", _siteNameBox, 0, required: true);
        AddRow(siteGroup, "住所", _siteAddressBox, 1);

        var adminGroup = new GroupBox
        {
            Text = "管理者アカウント",
            Location = new Point(24, 222),
            Size = new Size(510, 186),
        };
        AddRow(adminGroup, "ログインID", _loginIdBox, 0, required: true);
        AddRow(adminGroup, "表示名", _displayNameBox, 1, required: true);
        AddRow(adminGroup, "メールアドレス", _emailBox, 2, required: true);
        AddRow(adminGroup, "パスワード", _passwordBox, 3, required: true);
        AddRow(adminGroup, "パスワード（確認）", _passwordConfirmBox, 4, required: true);

        _registerButton.Location = new Point(394, 420);

        Controls.AddRange([heading, description, siteGroup, adminGroup, _registerButton]);
    }

    /// <summary>グループボックス内に「ラベル＋入力欄」の1行を配置する</summary>
    private static void AddRow(GroupBox group, string label, Control input, int rowIndex, bool required = false)
    {
        var y = 26 + (rowIndex * 30);

        var labelControl = UiTheme.CreateLabel(label, required);
        labelControl.Location = new Point(12, y);
        labelControl.Height = 26;

        input.Location = new Point(12 + UiTheme.LabelWidth + 8, y);

        group.Controls.Add(labelControl);
        group.Controls.Add(input);
    }

    /// <summary>拠点と初期管理者を登録する</summary>
    private async Task RegisterAsync()
    {
        if (!Validate(out var message))
        {
            UiTheme.ShowWarning(this, message);
            return;
        }

        try
        {
            _registerButton.Enabled = false;

            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var siteRepository = _provider.GetRequiredService<ISiteRepository>();
                var operatorRepository = _provider.GetRequiredService<IOperatorRepository>();

                var siteId = await siteRepository.CreateAsync(new Site
                {
                    SiteName = _siteNameBox.Text.Trim(),
                    Address = NullIfBlank(_siteAddressBox.Text),
                    LabelCompanyName = _siteNameBox.Text.Trim(),
                    LabelSizeType = LabelSizeType.Preset1,
                    IsActive = true,
                }).ConfigureAwait(true);

                // 初期管理者は RoleType・SiteId を必ず設定する（設計書 17章 確認事項No.4）
                await operatorRepository.CreateAsync(new Operator
                {
                    LoginId = _loginIdBox.Text.Trim(),
                    PasswordHash = PasswordHasher.Hash(_passwordBox.Text),
                    DisplayName = _displayNameBox.Text.Trim(),
                    Email = _emailBox.Text.Trim(),
                    RoleType = RoleType.Administrator,
                    SiteId = siteId,
                    IsActive = true,
                }).ConfigureAwait(true);
            }).ConfigureAwait(true);

            UiTheme.ShowInfo(this, "初期セットアップが完了しました。登録した管理者アカウントでログインしてください。");
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
        finally
        {
            _registerButton.Enabled = true;
        }
    }

    /// <summary>入力値を検証する</summary>
    private bool Validate(out string message)
    {
        if (string.IsNullOrWhiteSpace(_siteNameBox.Text))
        {
            message = "拠点名を入力してください。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_loginIdBox.Text))
        {
            message = "ログインIDを入力してください。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_displayNameBox.Text))
        {
            message = "表示名を入力してください。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(_emailBox.Text) || !_emailBox.Text.Contains('@', StringComparison.Ordinal))
        {
            message = "メールアドレスを正しく入力してください。予約登録者宛メールの送信先として使用します。";
            return false;
        }
        if (_passwordBox.Text.Length < 8)
        {
            message = "パスワードは8文字以上で入力してください。";
            return false;
        }
        if (_passwordBox.Text != _passwordConfirmBox.Text)
        {
            message = "パスワードと確認用パスワードが一致しません。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    /// <summary>空白のみの入力は null として扱う</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
