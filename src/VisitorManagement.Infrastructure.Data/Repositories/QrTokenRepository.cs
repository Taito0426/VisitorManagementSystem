using Dapper;
using VisitorManagement.Core.Abstractions;
using VisitorManagement.Core.Entities;

namespace VisitorManagement.Infrastructure.Data.Repositories;

/// <summary>
/// QRトークンの Repository 実装（設計書 9.2.4節）。
/// 1予約につき1行のみ、代表者の Visitors 行に紐づけて発行する。
/// </summary>
public sealed class QrTokenRepository(IDbSessionFactory sessionFactory, IDbRetryPolicy retryPolicy)
    : RepositoryBase(sessionFactory, retryPolicy), IQrTokenRepository
{
    private const string SelectColumns = """
        SELECT  TokenId, VisitorId, TokenValue, IssuedAt, ExpiresAt, UsedCount, ReissueCount, IsValid
        FROM    dbo.QRTokens
        """;

    /// <inheritdoc />
    public Task<Guid> CreateAsync(QrToken token, IDbSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (token.TokenId == Guid.Empty)
        {
            token.TokenId = Guid.NewGuid();
        }
        if (token.TokenValue == Guid.Empty)
        {
            // QRコードに埋め込む値。GUIDにより推測困難性を確保する（設計書 11.4節）
            token.TokenValue = Guid.NewGuid();
        }

        const string sql = """
            INSERT INTO dbo.QRTokens
                (TokenId, VisitorId, TokenValue, IssuedAt, ExpiresAt, UsedCount, ReissueCount, IsValid)
            VALUES
                (@TokenId, @VisitorId, @TokenValue, sysutcdatetime(), @ExpiresAt, @UsedCount, @ReissueCount, @IsValid);
            """;

        return ExecuteAsync(session, async s =>
        {
            await s.Connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                token.TokenId,
                token.VisitorId,
                token.TokenValue,
                token.ExpiresAt,
                token.UsedCount,
                token.ReissueCount,
                token.IsValid,
            }, s.Transaction)).ConfigureAwait(false);

            return token.TokenId;
        });
    }

    /// <inheritdoc />
    public Task<QrToken?> GetByTokenValueAsync(Guid tokenValue, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<QrToken>(
            new CommandDefinition(
                $"{SelectColumns} WHERE TokenValue = @TokenValue;",
                new { TokenValue = tokenValue },
                s.Transaction)));

    /// <inheritdoc />
    public Task<QrToken?> GetByVisitorIdAsync(Guid visitorId, IDbSession? session = null)
        => ExecuteAsync(session, s => s.Connection.QuerySingleOrDefaultAsync<QrToken>(
            new CommandDefinition(
                $"{SelectColumns} WHERE VisitorId = @VisitorId;",
                new { VisitorId = visitorId },
                s.Transaction)));

    /// <inheritdoc />
    public Task UpdateUsedCountAsync(Guid tokenId, byte usedCount, IDbSession? session = null)
    {
        const string sql = "UPDATE dbo.QRTokens SET UsedCount = @UsedCount WHERE TokenId = @TokenId;";

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { TokenId = tokenId, UsedCount = usedCount }, s.Transaction)));
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeAsync(Guid tokenId, byte expectedUsedCount, IDbSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        // WHERE 句に現在値を含めることで、同時読取時に後発の更新を失敗させる
        const string sql = """
            UPDATE  dbo.QRTokens
            SET     UsedCount = UsedCount + 1
            WHERE   TokenId = @TokenId
                    AND UsedCount = @ExpectedUsedCount;
            """;

        var affected = await session.Connection.ExecuteAsync(new CommandDefinition(
            sql, new { TokenId = tokenId, ExpectedUsedCount = expectedUsedCount }, session.Transaction))
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <inheritdoc />
    public Task ExtendExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null)
    {
        // 期限切れ・無効トークンの再送信時：有効期限を再計算のうえ再有効化し、再発行回数を加算する（設計書 5.3節）
        const string sql = """
            UPDATE  dbo.QRTokens
            SET     ExpiresAt = @ExpiresAt,
                    IsValid = 1,
                    ReissueCount = CASE WHEN ReissueCount < 255 THEN ReissueCount + 1 ELSE ReissueCount END
            WHERE   TokenId = @TokenId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { TokenId = tokenId, ExpiresAt = expiresAt }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task UpdateExpirationAsync(Guid tokenId, DateTime expiresAt, IDbSession? session = null)
    {
        // 予約日時の変更に追随させるための更新。再発行ではないため ReissueCount は加算しない
        const string sql = "UPDATE dbo.QRTokens SET ExpiresAt = @ExpiresAt WHERE TokenId = @TokenId;";

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { TokenId = tokenId, ExpiresAt = expiresAt }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task IncrementReissueCountAsync(Guid tokenId, IDbSession? session = null)
    {
        // tinyint の上限（255）を超えないようガードする
        const string sql = """
            UPDATE  dbo.QRTokens
            SET     ReissueCount = CASE WHEN ReissueCount < 255 THEN ReissueCount + 1 ELSE ReissueCount END
            WHERE   TokenId = @TokenId;
            """;

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { TokenId = tokenId }, s.Transaction)));
    }

    /// <inheritdoc />
    public Task InvalidateAsync(Guid tokenId, IDbSession? session = null)
    {
        const string sql = "UPDATE dbo.QRTokens SET IsValid = 0 WHERE TokenId = @TokenId;";

        return ExecuteAsync(session, s => s.Connection.ExecuteAsync(
            new CommandDefinition(sql, new { TokenId = tokenId }, s.Transaction)));
    }
}
