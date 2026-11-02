using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Security;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// 拠点マスタ管理画面（設計書 8.1節 画面No.5、6.11節）。
/// 拠点の登録・編集と、当日受付ラベル印刷用の社名・ロゴ画像・ラベルサイズを設定する。管理者ロール専用。
/// </summary>
public sealed class SiteMasterForm : Form
{
    private readonly MasterDataService _masterDataService;
    private readonly ILabelPresetProvider _labelPresetProvider;
    private readonly OperatorContext _ctx;

    private readonly DataGridView _grid = UiTheme.CreateGrid();
    private readonly BindingSource _bindingSource = [];

    private readonly TextBox _siteNameBox = new() { Width = 260 };
    private readonly TextBox _addressBox = new() { Width = 400 };
    private readonly TextBox _labelCompanyNameBox = new() { Width = 400 };
    private readonly ComboBox _labelSizeBox = new() { Width = 400, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _isActiveBox = new() { Text = "有効（チェックを外すと廃止拠点になります）", AutoSize = true };
    private readonly PictureBox _logoPreview = new()
    {
        Size = new Size(160, 70),
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
    };
    private readonly Button _selectLogoButton = UiTheme.CreateButton("ロゴ選択", 100);
    private readonly Button _clearLogoButton = UiTheme.CreateButton("ロゴ削除", 100);

    private readonly Button _newButton = UiTheme.CreateButton("新規");
    private readonly Button _saveButton = UiTheme.CreateButton("保存");
    private readonly Button _closeButton = UiTheme.CreateButton("閉じる");

    private int? _editingSiteId;
    private byte[]? _logoImage;

    public SiteMasterForm(IServiceProvider provider)
    {
        _masterDataService = provider.GetRequiredService<MasterDataService>();
        _labelPresetProvider = provider.GetRequiredService<ILabelPresetProvider>();
        _ctx = provider.GetRequiredService<AppSession>().Current;

        Text = $"{UiTheme.AppName} - 拠点マスタ管理";
        ClientSize = new Size(980, 720);
        MinimumSize = new Size(860, 620);
        UiTheme.Apply(this);
        StartPosition = FormStartPosition.CenterParent;

        BuildLayout();

        Load += async (_, _) => await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        _grid.Columns.AddRange(
            UiTheme.TextColumn("拠点名", nameof(SiteRow.SiteName), 160),
            UiTheme.TextColumn("住所", nameof(SiteRow.Address), 260, fill: true),
            UiTheme.TextColumn("ラベル表示社名", nameof(SiteRow.LabelCompanyName), 220),
            UiTheme.TextColumn("ラベルサイズ", nameof(SiteRow.LabelSizeText), 170),
            UiTheme.TextColumn("ロゴ", nameof(SiteRow.LogoText), 70),
            UiTheme.TextColumn("状態", nameof(SiteRow.StatusText), 70));
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => LoadSelection();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 12, 0) };
        gridPanel.Controls.Add(_grid);

        var editGroup = new GroupBox
        {
            Dock = DockStyle.Bottom,
            Height = 280,
            Text = "登録・編集",
            Padding = new Padding(12),
        };

        AddRow(editGroup, "拠点名", _siteNameBox, 0, required: true);
        AddRow(editGroup, "住所", _addressBox, 1);
        AddRow(editGroup, "ラベル表示社名", _labelCompanyNameBox, 2);
        AddRow(editGroup, "ラベルサイズ", _labelSizeBox, 3);

        var logoLabel = UiTheme.CreateLabel("ラベル用ロゴ");
        logoLabel.Location = new Point(12, 26 + (4 * 34));
        logoLabel.Height = 26;
        _logoPreview.Location = new Point(12 + UiTheme.LabelWidth + 8, 26 + (4 * 34));
        _selectLogoButton.Location = new Point(12 + UiTheme.LabelWidth + 180, 26 + (4 * 34));
        _clearLogoButton.Location = new Point(12 + UiTheme.LabelWidth + 290, 26 + (4 * 34));
        _isActiveBox.Location = new Point(12 + UiTheme.LabelWidth + 8, 26 + (4 * 34) + 78);

        editGroup.Controls.AddRange([logoLabel, _logoPreview, _selectLogoButton, _clearLogoButton, _isActiveBox]);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            Padding = new Padding(12, 8, 12, 8),
        };
        buttonPanel.Controls.AddRange([_newButton, _saveButton, _closeButton]);

        _selectLogoButton.Click += (_, _) => SelectLogo();
        _clearLogoButton.Click += (_, _) => SetLogo(null);
        _newButton.Click += (_, _) => ClearInput();
        _saveButton.Click += async (_, _) => await SaveAsync().ConfigureAwait(true);
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

    /// <summary>一覧とラベルサイズの選択肢を読み込む</summary>
    private async Task LoadAsync()
    {
        try
        {
            if (_labelSizeBox.Items.Count == 0)
            {
                // プリセット2・3は導入時ヒアリングで個別設定するため、未設定の場合はその旨を表示する
                foreach (var preset in _labelPresetProvider.GetAllPresets())
                {
                    var text = preset.IsDefined
                        ? $"プリセット{(int)preset.SizeType}: 幅{preset.WidthMm}mm × 高さ{preset.HeightMm}mm"
                          + (string.IsNullOrWhiteSpace(preset.LabelPartNumber) ? string.Empty : $"（{preset.LabelPartNumber}）")
                        : $"プリセット{(int)preset.SizeType}: 未設定（導入時ヒアリングで個別設定）";

                    _labelSizeBox.Items.Add(new ComboItem<LabelSizeType>(text, preset.SizeType));
                }
            }

            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                var sites = await _masterDataService.GetSitesAsync(activeOnly: false).ConfigureAwait(true);
                _bindingSource.DataSource = sites.Select(s => new SiteRow(s, _labelPresetProvider)).ToList();
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
        if (_grid.CurrentRow?.DataBoundItem is not SiteRow row)
        {
            return;
        }

        var site = row.Source;
        _editingSiteId = site.SiteId;
        _siteNameBox.Text = site.SiteName;
        _addressBox.Text = site.Address;
        _labelCompanyNameBox.Text = site.LabelCompanyName;
        _isActiveBox.Checked = site.IsActive;
        SelectLabelSize(site.LabelSizeType);
        SetLogo(site.LabelLogoImage);
    }

    /// <summary>入力欄をクリアして新規登録状態にする</summary>
    private void ClearInput()
    {
        _editingSiteId = null;
        _siteNameBox.Clear();
        _addressBox.Clear();
        _labelCompanyNameBox.Clear();
        _isActiveBox.Checked = true;
        SelectLabelSize(LabelSizeType.Preset1);
        SetLogo(null);
        _siteNameBox.Focus();
    }

    /// <summary>ラベルサイズの選択を指定プリセットに合わせる</summary>
    private void SelectLabelSize(LabelSizeType sizeType)
    {
        for (var i = 0; i < _labelSizeBox.Items.Count; i++)
        {
            if (_labelSizeBox.Items[i] is ComboItem<LabelSizeType> item && item.Value == sizeType)
            {
                _labelSizeBox.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>ロゴ画像ファイルを選択する</summary>
    private void SelectLogo()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "ラベル印刷用ロゴ画像の選択",
            Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);

            // ラベル印刷用の小さな画像を想定しているため、過大なファイルは拒否する
            const int maxBytes = 2 * 1024 * 1024;
            if (bytes.Length > maxBytes)
            {
                UiTheme.ShowWarning(this, "ロゴ画像は2MB以下のファイルを選択してください。");
                return;
            }

            SetLogo(bytes);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>ロゴ画像を設定してプレビューを更新する</summary>
    private void SetLogo(byte[]? bytes)
    {
        _logoImage = bytes;

        _logoPreview.Image?.Dispose();
        _logoPreview.Image = null;

        if (bytes is not { Length: > 0 })
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            _logoPreview.Image = Image.FromStream(stream);
        }
        catch (ArgumentException)
        {
            // 画像として読み込めない場合はプレビューなしで保持する
        }
    }

    /// <summary>入力内容を保存する</summary>
    private async Task SaveAsync()
    {
        var site = new Site
        {
            SiteId = _editingSiteId ?? 0,
            SiteName = _siteNameBox.Text.Trim(),
            Address = NullIfBlank(_addressBox.Text),
            LabelCompanyName = NullIfBlank(_labelCompanyNameBox.Text),
            LabelLogoImage = _logoImage,
            LabelSizeType = (_labelSizeBox.SelectedItem as ComboItem<LabelSizeType>)?.Value ?? LabelSizeType.Preset1,
            IsActive = _isActiveBox.Checked,
        };

        try
        {
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
            {
                if (_editingSiteId is null)
                {
                    await _masterDataService.CreateSiteAsync(site, _ctx).ConfigureAwait(true);
                }
                else
                {
                    await _masterDataService.UpdateSiteAsync(site, _ctx).ConfigureAwait(true);
                }
            }).ConfigureAwait(true);

            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>空白のみの入力は null として扱う</summary>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logoPreview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>拠点一覧の表示行</summary>
    private sealed class SiteRow(Site source, ILabelPresetProvider presetProvider)
    {
        public Site Source { get; } = source;

        public string SiteName => Source.SiteName;

        public string? Address => Source.Address;

        public string? LabelCompanyName => Source.LabelCompanyName;

        public string LabelSizeText
        {
            get
            {
                var preset = presetProvider.GetPreset(Source.LabelSizeType);
                return preset.IsDefined
                    ? $"{preset.WidthMm}mm × {preset.HeightMm}mm"
                    : $"プリセット{(int)preset.SizeType}（未設定）";
            }
        }

        public string LogoText => Source.LabelLogoImage is { Length: > 0 } ? "あり" : "-";

        public string StatusText => Source.IsActive ? "有効" : "廃止";
    }
}
