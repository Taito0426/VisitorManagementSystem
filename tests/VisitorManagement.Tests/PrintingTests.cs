using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;
using VisitorManagement.Core.Enums;
using VisitorManagement.Core.Settings;
using VisitorManagement.Infrastructure.Mail;
using VisitorManagement.Infrastructure.Printing;

namespace VisitorManagement.Tests;

/// <summary>
/// QRコード生成とラベルプリセットのテスト（設計書 6.2節・9.2.1節・18章 確認事項No.2）。
/// </summary>
public sealed class PrintingTests
{
    [Fact]
    public void QRコードはPNG形式で生成される()
    {
        var generator = new QrCodeImageGenerator();

        var png = generator.GeneratePng(Guid.NewGuid());

        // PNG シグネチャ
        Assert.True(png.Length > 0);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }

    [Fact]
    public void QRコードはトークン値ごとに異なる画像になる()
    {
        var generator = new QrCodeImageGenerator();

        var first = generator.GeneratePng(Guid.NewGuid());
        var second = generator.GeneratePng(Guid.NewGuid());

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void モジュールサイズに0以下を指定すると例外になる()
    {
        var generator = new QrCodeImageGenerator();

        Assert.Throws<ArgumentOutOfRangeException>(() => generator.GeneratePng(Guid.NewGuid(), 0));
    }

    [Fact]
    public void プリセット1は幅60mm高さ110mmが標準搭載される()
    {
        var provider = new LabelPresetProvider(Options.Create(new PrintingSettings()));

        var preset = provider.GetPreset(LabelSizeType.Preset1);

        Assert.True(preset.IsDefined);
        Assert.Equal(60, preset.WidthMm);
        Assert.Equal(110, preset.HeightMm);
    }

    [Fact]
    public void プリセット2と3は既定では未設定である()
    {
        var provider = new LabelPresetProvider(Options.Create(new PrintingSettings()));

        // 導入時ヒアリングにより個別設定する空の設定項目（設計書 18章 確認事項No.2）
        Assert.False(provider.GetPreset(LabelSizeType.Preset2).IsDefined);
        Assert.False(provider.GetPreset(LabelSizeType.Preset3).IsDefined);
    }

    [Fact]
    public void 設定ファイルでプリセット2の寸法を個別設定できる()
    {
        var settings = new PrintingSettings();
        settings.LabelPresets.Preset2 = new LabelSizeSettings { WidthMm = 50, HeightMm = 80, LabelPartNumber = "DK-1202" };

        var provider = new LabelPresetProvider(Options.Create(settings));
        var preset = provider.GetPreset(LabelSizeType.Preset2);

        Assert.True(preset.IsDefined);
        Assert.Equal(50, preset.WidthMm);
        Assert.Equal("DK-1202", preset.LabelPartNumber);
    }

    [Fact]
    public void 全プリセットを取得すると3件返る()
    {
        var provider = new LabelPresetProvider(Options.Create(new PrintingSettings()));

        Assert.Equal(3, provider.GetAllPresets().Count);
    }

    [Fact]
    public async Task プレビューPDFはA4一枚で生成される()
    {
        var pdf = await GeneratePreviewAsync(companionCount: 3);

        // PDF シグネチャ
        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public async Task 同行者が多くてもプレビューPDFの生成は失敗しない()
    {
        // メール文面が長くなると本文フォントの自動縮小・省略が働く（下半分に収める）
        var pdf = await GeneratePreviewAsync(companionCount: 30);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    [Fact]
    public async Task 予約登録者が未取得でもプレビューPDFを生成できる()
    {
        var pdf = await GeneratePreviewAsync(companionCount: 0, withRegistrant: false);

        Assert.Equal("%PDF"u8.ToArray(), pdf[..4]);
    }

    /// <summary>プレビューPDFを生成する（テスト用の予約グループを組み立てる）</summary>
    private static Task<byte[]> GeneratePreviewAsync(int companionCount, bool withRegistrant = true)
    {
        var representative = new Visitor
        {
            VisitorId = Guid.NewGuid(),
            IsRepresentative = true,
            SiteId = 1,
            CompanyName = "株式会社サンプル商事",
            VisitorName = "山田 太郎",
            VisitorEmail = "taro.yamada@example.co.jp",
            VisitDate = new DateOnly(2026, 9, 3),
            VisitTimeFrom = new TimeOnly(14, 0),
            VisitTimeTo = new TimeOnly(15, 30),
            HostEmployeeName = "営業部 田中 一郎",
            PurposeOfVisit = "新製品のお打ち合わせ",
            SiteName = "東京本社",
        };

        var group = new ReservationGroup
        {
            ReservationGroupId = Guid.NewGuid(),
            Representative = representative,
            Companions = [.. Enumerable.Range(1, companionCount)
                .Select(i => new Visitor { VisitorName = $"同行者{i}", CompanyName = "株式会社サンプル商事" })],
            Token = new QrToken { TokenValue = Guid.NewGuid(), ExpiresAt = DateTime.UtcNow.AddHours(2) },
            RegisteredBy = withRegistrant
                ? new Operator { DisplayName = "受付 花子", Email = "reception@example.co.jp" }
                : null,
        };

        var site = new Site { SiteId = 1, SiteName = "東京本社", LabelSizeType = LabelSizeType.Preset1 };

        var settings = Options.Create(new PrintingSettings());
        var printer = new DemoPdfLabelPrinter(
            settings,
            new LabelPresetProvider(settings),
            new MailBodyComposer(),
            NullLogger<DemoPdfLabelPrinter>.Instance);

        var qrCodePng = new QrCodeImageGenerator().GeneratePng(group.Token.TokenValue);
        return printer.GeneratePreviewPdfAsync(group, qrCodePng, site);
    }
}
