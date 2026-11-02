using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// ログイン画面（設計書 8.1節 画面No.1）。
/// 操作ユーザーID・パスワードによる認証を行う（設計書 11.1節）。
/// </summary>
public sealed class LoginForm : Form
{
    private readonly IServiceProvider _provider;

    private readonly TextBox _loginIdBox = new() { Width = 200 };
    private readonly TextBox _passwordBox = new() { Width = 200, UseSystemPasswordChar = true };
    private readonly Label _messageLabel = new()
    {
        AutoSize = false,
        Size = new Size(340, 36),
        ForeColor = Color.FromArgb(180, 30, 30),
    };
    private readonly Button _loginButton = UiTheme.CreateButton("ログイン", 100);
    private readonly Button _cancelButton = UiTheme.CreateButton("終了", 100);

    public LoginForm(IServiceProvider provider)
    {
        _provider = provider;

        Text = $"{UiTheme.AppName} - ログイン";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 260);
        StartPosition = FormStartPosition.CenterScreen;
        UiTheme.Apply(this);

        BuildLayout();

        _loginButton.Click += async (_, _) => await LoginAsync().ConfigureAwait(true);
        _cancelButton.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = _loginButton;
        CancelButton = _cancelButton;
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout() //★レイアウト変更
    {
        var heading = new Label
        {
            Text = UiTheme.AppName,
            Font = UiTheme.HeadingFont,
            AutoSize = true,
            Location = new Point(30, 26),
        };

        var loginIdLabel = UiTheme.CreateLabel("ログインID");
        loginIdLabel.Location = new Point(30, 78);
        _loginIdBox.Location = new Point(30 + UiTheme.LabelWidth, 78);

        var passwordLabel = UiTheme.CreateLabel("パスワード");
        passwordLabel.Location = new Point(30, 114);
        _passwordBox.Location = new Point(30 + UiTheme.LabelWidth, 114);

        _messageLabel.Location = new Point(30, 150);

        _loginButton.Location = new Point(160, 200);
        _cancelButton.Location = new Point(288, 200);

        Controls.AddRange([heading, loginIdLabel, _loginIdBox, passwordLabel, _passwordBox, _messageLabel, _loginButton, _cancelButton]);
    }

    /// <summary>ログインを実行する</summary>
    private async Task LoginAsync()
    {
        _messageLabel.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(_loginIdBox.Text) || string.IsNullOrEmpty(_passwordBox.Text))
        {
            _messageLabel.Text = "ログインIDとパスワードを入力してください。";
            return;
        }

        try
        {
            _loginButton.Enabled = false;

            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var authentication = _provider.GetRequiredService<AuthenticationService>();
                var context = await authentication
                    .LoginAsync(_loginIdBox.Text.Trim(), _passwordBox.Text).ConfigureAwait(true);

                if (context is null)
                {
                    // 認証失敗の理由（ID誤り／パスワード誤り／無効化）は区別して提示しない
                    _messageLabel.Text = "ログインIDまたはパスワードが正しくありません。";
                    _passwordBox.Clear();
                    _passwordBox.Focus();
                    return;
                }

                _provider.GetRequiredService<AppSession>().SignIn(context);
                DialogResult = DialogResult.OK;
                Close();
            }).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
        finally
        {
            _loginButton.Enabled = true;
        }
    }
}
