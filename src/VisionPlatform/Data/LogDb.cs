using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using VisionPlatform.Core;

namespace VisionPlatform.Data
{
    /// <summary>
    /// 日志数据库：把运行日志（含 OK / NG 判定与软件报错 / 异常堆栈）持久化到 SQLite，
    /// 支持按日期范围 + 级别查询、每日汇总、按保留天数清理。
    /// <para>
    /// 写盘走<strong>后台批处理线程</strong>（并入事务后批量 INSERT），不阻塞检测 / UI 线程；
    /// 查询用独立的只读连接，与写线程互不干扰（WAL 模式支持并发读写）。
    /// </para>
    /// </summary>
    public static class LogDb
    {
        private static readonly object WriteLock = new object();
        private static Sqlite.Connection _writeConn;
        private static readonly ConcurrentQueue<LogEntry> Queue = new ConcurrentQueue<LogEntry>();
        private static Thread _worker;
        private static volatile bool _running;
        private static string _path = "";

        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private const string CreateSql = @"
CREATE TABLE IF NOT EXISTS logs(
  id     INTEGER PRIMARY KEY AUTOINCREMENT,
  ts     INTEGER NOT NULL,
  time   TEXT    NOT NULL,
  level  TEXT    NOT NULL,
  source TEXT,
  message TEXT,
  detail TEXT
);
CREATE INDEX IF NOT EXISTS idx_logs_ts    ON logs(ts);
CREATE INDEX IF NOT EXISTS idx_logs_level ON logs(level);";

        /// <summary>初始化日志库：打开库、建表、启动后台写线程、按保留天数清理一次。</summary>
        public static void Initialize(string dbPath, int retentionDays)
        {
            string path = string.IsNullOrWhiteSpace(dbPath) ? AppPaths.LogDbFile : dbPath.Trim();
            _path = path;

            lock (WriteLock)
            {
                try
                {
                    // 【先显式初始化 SQLite，勿删】否则 sqlite3_open 的惰性自动初始化在 .NET 宿主里会触发段错误。
                    Sqlite.EnsureInitialized();
                    _writeConn = new Sqlite.Connection();
                    _writeConn.Open(path);
                    _writeConn.Execute("PRAGMA journal_mode=WAL;");
                    _writeConn.Execute("PRAGMA synchronous=NORMAL;");
                    _writeConn.Execute(CreateSql);
                }
                catch (Exception ex)
                {
                    Logger.Error("日志数据库初始化失败：" + ex.Message);
                    if (_writeConn != null) { _writeConn.Dispose(); _writeConn = null; }
                }
            }

            _running = true;
            _worker = new Thread(delegate() { PerfProbe.Mark("日志库写盘"); WorkerLoop(); })
            {
                IsBackground = true,
                Name = "LogDbWriter"
            };
            _worker.Start();

            if (retentionDays > 0) CleanupRetention(retentionDays);
        }

        /// <summary>入队一条日志（异步落库）。库未就绪时静默丢弃，不影响主流程。</summary>
        public static void Enqueue(LogEntry e)
        {
            if (!_running || _writeConn == null || e == null) return;
            Queue.Enqueue(e);
        }

        private static void WorkerLoop()
        {
            while (_running)
            {
                try { Flush(); }
                catch (Exception ex) { Logger.Error("日志批量写入异常：" + ex.Message); }

                // 每 ~500ms 刷一次；退出前最后一次 flush 在循环外补做
                for (int i = 0; i < 5 && _running; i++)
                    Thread.Sleep(100);
            }

            try { Flush(); } catch (Exception ex) { Logger.Error("日志退出前写入异常：" + ex.Message); }
        }

        private static void Flush()
        {
            if (Queue.IsEmpty) return;

            lock (WriteLock)
            {
                if (_writeConn == null) return;

                using (var stmt = _writeConn.Prepare(
                    "INSERT INTO logs(ts,time,level,source,message,detail) VALUES(?,?,?,?,?,?)"))
                {
                    _writeConn.Execute("BEGIN;");
                    try
                    {
                        LogEntry e;
                        int count = 0;
                        while (Queue.TryDequeue(out e) && count < 2000)
                        {
                            stmt.Reset();
                            stmt.BindInt64(1, ToUnixMs(e.Time));
                            stmt.BindText(2, e.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                            stmt.BindText(3, e.LevelText);
                            stmt.BindText(4, e.FlowName ?? "");
                            stmt.BindText(5, e.Message ?? "");
                            stmt.BindText(6, e.Detail ?? "");
                            stmt.Step();    // INSERT 返回 DONE
                            count++;
                        }
                        _writeConn.Execute("COMMIT;");
                    }
                    catch
                    {
                        try { _writeConn.Execute("ROLLBACK;"); } catch { }
                        throw;
                    }
                }
            }
        }

        /// <summary>按保留天数删除过期日志（ts 早于截止点的记录）。</summary>
        public static void CleanupRetention(int days)
        {
            if (days <= 0) return;
            long cutoff = ToUnixMs(DateTime.UtcNow.Date.AddDays(-days));

            lock (WriteLock)
            {
                if (_writeConn == null) return;
                try { _writeConn.Execute("DELETE FROM logs WHERE ts < " + cutoff); }
                catch (Exception ex) { Logger.Error("日志清理失败：" + ex.Message); }
            }
        }

        /// <summary>停止后台写线程并关闭连接（程序退出时调用）。</summary>
        public static void Shutdown()
        {
            _running = false;
            if (_worker != null) { try { _worker.Join(2000); } catch { } _worker = null; }
            lock (WriteLock)
            {
                if (_writeConn != null) { _writeConn.Dispose(); _writeConn = null; }
            }
        }

        // ---------------------- 查询（独立只读连接）----------------------

        /// <summary>每日汇总行：日期 / 检测数 / OK / NG / 软件报错数。</summary>
        public sealed class DailyRow
        {
            public string Day = "";
            public int Total;
            public int Ok;
            public int Ng;
            public int Error;
            public double Yield { get { return Total > 0 ? Math.Round(Ok * 100.0 / Total, 1) : 0.0; } }
        }

        /// <summary>明细行：时间 / 归属流程 / 级别 / 消息 / 详情（异常堆栈）。</summary>
        public sealed class LogRow
        {
            public string Time = "";
            public string Source = "";
            public string Level = "";
            public string Message = "";
            public string Detail = "";
        }

        private static long ToUnixMs(DateTime dt)
        {
            return (long)(dt.ToUniversalTime() - Epoch).TotalMilliseconds;
        }

        /// <summary>每日汇总（按日期升序）。levelFilter 为空表示不限级别。</summary>
        public static List<DailyRow> QueryDaily(DateTime from, DateTime to, string levelFilter)
        {
            var list = new List<DailyRow>();
            long fromMs = ToUnixMs(from.Date);
            long toMs = ToUnixMs(to.Date.AddDays(1).AddTicks(-1));

            try
            {
                using (var conn = OpenRead())
                using (var stmt = conn.Prepare(
                    "SELECT substr(time,1,10) AS day," +
                           " SUM(CASE WHEN level IN ('OK','NG') THEN 1 ELSE 0 END)," +
                           " SUM(CASE WHEN level='OK' THEN 1 ELSE 0 END)," +
                           " SUM(CASE WHEN level='NG' THEN 1 ELSE 0 END)," +
                           " SUM(CASE WHEN level='ERROR' THEN 1 ELSE 0 END)" +
                    " FROM logs WHERE ts BETWEEN " + fromMs + " AND " + toMs +
                    LevelClause(levelFilter) +
                    " GROUP BY day ORDER BY day"))
                {
                    while (stmt.Step())
                        list.Add(new DailyRow
                        {
                            Day = stmt.ColumnText(0),
                            Total = (int)stmt.ColumnInt64(1),
                            Ok = (int)stmt.ColumnInt64(2),
                            Ng = (int)stmt.ColumnInt64(3),
                            Error = (int)stmt.ColumnInt64(4)
                        });
                }
            }
            catch (Exception ex) { Logger.Error("日志汇总查询失败：" + ex.Message); }

            return list;
        }

        /// <summary>明细（按时间倒序，最多 3000 条）。levelFilter 为空表示不限级别。</summary>
        public static List<LogRow> QueryDetail(DateTime from, DateTime to, string levelFilter)
        {
            var list = new List<LogRow>();
            long fromMs = ToUnixMs(from.Date);
            long toMs = ToUnixMs(to.Date.AddDays(1).AddTicks(-1));

            try
            {
                using (var conn = OpenRead())
                using (var stmt = conn.Prepare(
                    "SELECT time,source,level,message,detail FROM logs" +
                    " WHERE ts BETWEEN " + fromMs + " AND " + toMs +
                    LevelClause(levelFilter) +
                    " ORDER BY ts DESC LIMIT 3000"))
                {
                    while (stmt.Step())
                        list.Add(new LogRow
                        {
                            Time = stmt.ColumnText(0),
                            Source = stmt.ColumnText(1),
                            Level = stmt.ColumnText(2),
                            Message = stmt.ColumnText(3),
                            Detail = stmt.ColumnText(4)
                        });
                }
            }
            catch (Exception ex) { Logger.Error("日志明细查询失败：" + ex.Message); }

            return list;
        }

        private static Sqlite.Connection OpenRead()
        {
            var conn = new Sqlite.Connection();
            conn.Open(_path);
            return conn;
        }

        /// <summary>级别筛选子句；level 取自 LogEntry.LevelText 固定文本（OK/NG/ERROR/INFO/WARN），非用户输入，安全。</summary>
        private static string LevelClause(string levelFilter)
        {
            if (string.IsNullOrWhiteSpace(levelFilter)) return "";
            return " AND level='" + levelFilter + "'";
        }
    }
}
