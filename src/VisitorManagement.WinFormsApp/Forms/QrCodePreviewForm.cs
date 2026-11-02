using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using VisitorManagement.Application.Services;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Dtos;

namespace VisitorManagement.WinFormsApp.Forms;

/// <summary>
/// QRコード表示画面。
/// 代表者に発行されたQRコード（設計書 6.2節）を画面上で確認し、画像として保存できる。
/// メール未着時に、受付担当者が内容を確認する用途を想定している。
/// デモ版（Printing:Provider="PdfPreview"）では、ラベルとメール文面のレイアウトを
/// A4 PDFとして出力して確認できる（設計書 19.4節）。
/// </summary>
public sealed class QrCodePreviewForm : Form
{
    private readonly IServiceProvider _provider;
    private readonly byte[] _qrCodePng;
    private readonly ReservationGroup _group;

    /// <summary>デモ版のPDFプレビュー（本番の実機印刷構成では null）</summary>
    private readonly ILabelPdfPreview? _labelPdfPreview;

    public QrCodePreviewForm(IServiceProvider provider, ReservationGroup group, byte[] qrCodePng)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(qrCodePng);

        _provider = provider;
        _group = group;
        _qrCodePng = qrCodePng;
        _labelPdfPreview = provider.GetService<ILabelPdfPreview>();

        Text = $"{UiTheme.AppName} - QRコード";
        ClientSize = new Size(560, 560);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        UiTheme.Apply(this);

        BuildLayout();
    }

    /// <summary>画面を組み立てる</summary>
    private void BuildLayout()
    {
        var representative = _group.Representative;

        var pictureBox = new PictureBox
        {
            Location = new Point(140, 20),
            Size = new Size(280, 280),
            SizeMode = PictureBoxSizeMode.Zoom,
            BorderStyle = BorderStyle.FixedSingle,
        };

        using (var stream = new MemoryStream(_qrCodePng))
        {
            pictureBox.Image = Image.FromStream(stream);
        }

        var infoLines = new List<string>
        {
            $"代表者　　: {representative.VisitorName}",
            $"会社名　　: {representative.CompanyName ?? "-"}",
            $"来館日時　: {representative.VisitDate:yyyy/MM/dd} {representative.VisitTimeFrom:HH\\:mm}",
            $"来館先拠点: {representative.SiteName ?? "-"}",
        };

        if (_group.Companions.Count > 0)
        {
            infoLines.Add($"同行者　　: {string.Join("、", _group.Companions.Select(c => c.VisitorName))}");
        }

        if (_group.Token is { } token)
        {
            infoLines.Add($"有効期限　: {token.ExpiresAt.ToLocalTime():yyyy/MM/dd HH:mm} まで");
            infoLines.Add($"読取回数　: {token.UsedCount} 回（1回目=入館 / 2回目=退館）");
        }

        var infoLabel = new Label
        {
            Text = string.Join(Environment.NewLine, infoLines),
            Location = new Point(24, 316),
            Size = new Size(512, 150),
            TextAlign = ContentAlignment.TopLeft,
        };

        var noteLabel = new Label
        {
            Text = "※ QRコードは代表者1名にのみ発行され、1枚でグループ全員の入退館を受け付けます。",
            Location = new Point(24, 466),
            Size = new Size(512, 34),
            ForeColor = Color.FromArgb(80, 80, 80),
        };

        var saveButton = UiTheme.CreateButton("画像を保存", 130);
        saveButton.Location = new Point(90, 508);
        saveButton.Click += (_, _) => SaveImage();

        var closeButton = UiTheme.CreateButton("閉じる", 120);
        closeButton.Location = new Point(400, 508);
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([pictureBox, infoLabel, noteLabel, saveButton, closeButton]);

        // デモ版のみ、ラベル＋メール文面のレイアウトをA4 PDFで確認できるようにする
        if (_labelPdfPreview is not null)
        {
            var pdfButton = UiTheme.CreateButton("レイアウトをPDFで確認", 160);
            pdfButton.Location = new Point(230, 508);
            pdfButton.Click += async (_, _) => await ExportPdfAsync(pdfButton).ConfigureAwait(true);
            Controls.Add(pdfButton);
        }

        CancelButton = closeButton;
    }

    /// <summary>QRコード画像をファイルへ保存する</summary>
    private void SaveImage()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "QRコード画像の保存",
            Filter = "PNG画像 (*.png)|*.png",
            FileName = $"qrcode-{_group.Representative.VisitorName}-{_group.Representative.VisitDate:yyyyMMdd}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, _qrCodePng);
            UiTheme.ShowInfo(this, "QRコード画像を保存しました。");
        }
        catch (Exception ex)
        {
            UiTheme.ShowError(this, ex);
        }
    }

    /// <summary>
    /// ラベルとメール文面のレイアウトをA4 PDFへ出力し、既定のビューアで開く（設計書 19.4節）。
    /// </summary>
    private async Task ExportPdfAsync(Button trigger)
    {
        if (_labelPdfPreview is null)
        {
            return;
        }

        try
        {
            trigger.Enabled = false;

            var masterDataService = _provider.GetRequiredService<MasterDataService>();
            var site = await masterDataService
                .GetSiteAsync(_group.Representative.SiteId).ConfigureAwait(true);

            if (site is null)
            {
                UiTheme.ShowWarning(this, "来館先拠点の情報を取得できないため、PDFを出力できません。");
                return;
            }

            byte[]? pdf = null;
            await UiTheme.RunWithWaitCursorAsync(this, async () =>
                pdf = await _labelPdfPreview.GeneratePreviewPdfAsync(_group, _qrCodePng, site).ConfigureAwait(true))
                .ConfigureAwait(true);

            var filePath = Path.Combine(
                Path.GetTempPath(),
                $"layout-preview-{_group.ReservationGroupId:N}.pdf");

            await File.WriteAllBytesAsync(filePath, pdf!).ConfigureAwait(true);

            // 関連付けられたPDFビューアで開く（未関連付けの場合は保存先を案内する）
            try
            {
                using var process = Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
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
            trigger.Enabled = true;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var control in Controls.OfType<PictureBox>())
            {
                control.Image?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
