using System.Data;
using Dapper;

namespace VisitorManagement.Infrastructure.Data;

/// <summary>
/// Dapper の型マッピング設定。アプリケーション起動時に一度だけ <see cref="Configure"/> を呼び出す。
/// </summary>
public static class DapperConfiguration
{
    private static readonly Lock Gate = new();
    private static bool _configured;

    /// <summary>
    /// DateOnly / TimeOnly / DateTime(UTC) のマッピングを登録する（冪等）。
    /// </summary>
    public static void Configure()
    {
        lock (Gate)
        {
            if (_configured)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
            SqlMapper.AddTypeHandler(new UtcDateTimeTypeHandler());
            _configured = true;
        }
    }
}

/// <summary>
/// date 列と <see cref="DateOnly"/> の相互変換（Visitors.VisitDate）。
/// </summary>
internal sealed class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value.ToDateTime(TimeOnly.MinValue);
    }

    public override DateOnly Parse(object value) => value switch
    {
        DateOnly d => d,
        DateTime dt => DateOnly.FromDateTime(dt),
        string s => DateOnly.Parse(s),
        _ => throw new DataException($"date 列を DateOnly に変換できません: {value?.GetType().Name}"),
    };
}

/// <summary>
/// time 列と <see cref="TimeOnly"/> の相互変換（Visitors.VisitTimeFrom / VisitTimeTo）。
/// </summary>
internal sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value.ToTimeSpan();
    }

    public override TimeOnly Parse(object value) => value switch
    {
        TimeOnly t => t,
        TimeSpan ts => TimeOnly.FromTimeSpan(ts),
        DateTime dt => TimeOnly.FromDateTime(dt),
        string s => TimeOnly.Parse(s),
        _ => throw new DataException($"time 列を TimeOnly に変換できません: {value?.GetType().Name}"),
    };
}

/// <summary>
/// datetime2 列の UTC 往復を保証する（設計書 9.2.3節：日時はUTCで保持する）。
/// SQL Server は datetime2 に種別（Kind）を保持しないため、読取時に UTC を明示する。
/// </summary>
internal sealed class UtcDateTimeTypeHandler : SqlMapper.TypeHandler<DateTime>
{
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.DateTime2;
        // ローカル時刻で渡された場合も必ずUTCに正規化してから保存する
        parameter.Value = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
    }

    public override DateTime Parse(object value) => value switch
    {
        DateTime dt => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        DateTimeOffset dto => dto.UtcDateTime,
        string s => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc),
        _ => throw new DataException($"datetime2 列を DateTime に変換できません: {value?.GetType().Name}"),
    };
}
