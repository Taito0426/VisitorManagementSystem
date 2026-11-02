using VisitorManagement.Core.Dtos;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Core.Abstractions;

/// <summary>
/// ラベル印刷の共通契約（設計書 19.4節。本番/デモで実装を切り替える）。
/// </summary>
public interface ILabelPrinter
{
    // 本番: Ql800LabelPrinter（b-PAC SDK経由で実機に印刷。代表者の情報のみ使用する）
    // デモ: DemoPdfLabelPrinter（A4 PDFとして生成しプレビュー表示。メール文面の描画に同行者・予約登録者も使用する）
    Task PrintQrLabelAsync(ReservationGroup group, byte[] qrCodePng, Site site);
}

/// <summary>
/// デモ版（DemoPdfLabelPrinter）がA4 PDFのプレビューを提供するための追加契約。
/// 受付Web画面・管理PCアプリから生成済みPDFを取得して表示・保存できるようにする。
/// </summary>
public interface ILabelPdfPreview
{
    /// <summary>
    /// A4 1枚のプレビューPDFを生成し、バイト列を返す。
    /// 上半分にラベル（QRコード）レイアウト、下半分に来館案内メールの文面を配置する。
    /// メール文面の組み立てには同行者・予約登録者の情報が必要なため、予約グループ単位で受け取る。
    /// </summary>
    Task<byte[]> GeneratePreviewPdfAsync(ReservationGroup group, byte[] qrCodePng, Site site);
}
