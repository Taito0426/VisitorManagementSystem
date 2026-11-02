using VisitorManagement.Application.Exceptions;

namespace VisitorManagement.WinFormsApp;

/// <summary>
/// 管理画面共通の外観設定と、画面構築を簡潔にするためのヘルパー。
/// WinForms管理画面は日本語のみとする（設計書 2.5節・6.12節）。
/// </summary>
public static class UiTheme
{
    /// <summary>アプリケーション名（各フォームのタイトルに使用）</summary>
    public const string AppName = "来館者記録管理システム";

    /// <summary>標準フォント</summary>
    public static readonly Font BaseFont = new("Meiryo UI", 9F);

    /// <summary>見出しフォント</summary>
    public static readonly Font HeadingFont = new("Meiryo UI", 12F, FontStyle.Bold);

    /// <summary>受付結果など強調表示用の大きいフォント</summary>
    public static readonly Font LargeFont = new("Meiryo UI", 14F, FontStyle.Bold);

    /// <summary>ラベル列の幅</summary>
    public const int LabelWidth = 140;

    /// <summary>コントロールの標準高さ</summary>
    public const int RowHeight = 30;

    /// <summary>フォームへ共通の外観を適用する</summary>
    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.Font = BaseFont;
        form.StartPosition = FormStartPosition.CenterParent;
        form.BackColor = Color.White;
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>一覧表示用の DataGridView を生成する</summary>
    public static DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 32,
            Font = BaseFont,
        };

        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        grid.ColumnHeadersDefaultCellStyle.Font = BaseFont;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        grid.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 120, 215);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 252);

        return grid;
    }

    /// <summary>テキスト列を生成する</summary>
    public static DataGridViewTextBoxColumn TextColumn(string header, string propertyName, int width, bool fill = false)
        => new()
        {
            HeaderText = header,
            DataPropertyName = propertyName,
            Width = width,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable,
        };

    /// <summary>ラベルを生成する</summary>
    public static Label CreateLabel(string text, bool required = false)
        => new()
        {
            Text = required ? text + " *" : text,
            AutoSize = false,
            Width = LabelWidth,
            Height = RowHeight,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = required ? Color.FromArgb(160, 30, 30) : Color.Black,
        };

    /// <summary>操作ボタンを生成する</summary>
    public static Button CreateButton(string text, int width = 110)
        => new()
        {
            Text = text,
            Width = width,
            Height = RowHeight,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(4),
        };

    /// <summary>情報メッセージを表示する</summary>
    public static void ShowInfo(IWin32Window? owner, string message)
        => MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>警告メッセージを表示する</summary>
    public static void ShowWarning(IWin32Window? owner, string message)
        => MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    /// <summary>確認ダイアログを表示する</summary>
    public static bool Confirm(IWin32Window? owner, string message)
        => MessageBox.Show(owner, message, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>
    /// 例外の内容をユーザー向けメッセージとして表示する。
    /// 業務ルール違反・権限不足はそのまま提示し、それ以外は技術的詳細を伏せた案内とする。
    /// </summary>
    public static void ShowError(IWin32Window? owner, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception switch
        {
            BusinessRuleException or PermissionDeniedException => exception.Message,
            Microsoft.Data.SqlClient.SqlException =>
                "一時的にデータベースに接続できません。時間をおいて再度お試しください。",
            _ => $"処理中にエラーが発生しました。{Environment.NewLine}{exception.Message}",
        };

        MessageBox.Show(owner, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>処理中はカーソルを砂時計にする</summary>
    public static async Task RunWithWaitCursorAsync(Form form, Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(action);

        var previous = form.Cursor;
        form.Cursor = Cursors.WaitCursor;
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            form.Cursor = previous;
        }
    }
}
