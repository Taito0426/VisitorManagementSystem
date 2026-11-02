using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VisitorManagement.WinFormsApp.Logging;

/// <summary>
/// 管理アプリ用の簡易ファイルロガー。
/// コンソールを持たない WinExe のため、障害調査用にログをファイルへ出力する。
/// </summary>
public sealed class FileLoggerProvider(string logDirectory, LogLevel minimumLevel) : ILoggerProvider
{
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName, minimumLevel);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>1行をログファイルへ追記する（日付ごとにファイルを分割する）</summary>
    internal void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                var path = Path.Combine(
                    logDirectory, $"app-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // ログ出力の失敗で業務処理を止めない
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>ファイル出力を行うロガー本体</summary>
    private sealed class FileLogger(FileLoggerProvider provider, string categoryName, LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(" [").Append(ToShortName(logLevel)).Append("] ");
            sb.Append(categoryName).Append(" - ");
            sb.Append(formatter(state, exception));

            if (exception is not null)
            {
                sb.AppendLine().Append(exception);
            }

            provider.Write(sb.ToString());
        }

        /// <summary>ログレベルの短縮表記</summary>
        private static string ToShortName(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "---",
        };
    }
}
