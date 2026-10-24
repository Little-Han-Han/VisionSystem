using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VisionPlatform.Data
{
    /// <summary>
    /// 极简 SQLite 封装：纯 P/Invoke 调用原生 <c>sqlite3.dll</c>（cdecl / x64），
    /// 不引入任何 NuGet 包或托管包装程序集，契合项目「零第三方依赖」铁律。
    /// <para>原生 DLL 由 <c>ThirdParty/SQLite/sqlite3.dll</c>（官方 x64 构建，仅依赖 KERNEL32 + VC 运行时）提供，
    /// 构建时由 PostBuildEvent 复制进输出目录。</para>
    /// <para>【字符串一律用 <see cref="UnmanagedType.LPUTF8Str"/>】SQLite 的 C API 只认 UTF-8；
    /// 用 <c>string</c> + LPUTF8Str 由封送器转 UTF-8 且自动补 null 结尾，既支持中文路径/消息，
    /// 又避免手搓 <c>byte[]</c>（byte[] 与 out 参数混用曾在本项目实测触发原生访问违例）。</para>
    /// </summary>
    internal static class Sqlite
    {
        private const int SQLITE_OK = 0;
        private const int SQLITE_ROW = 100;
        private const int SQLITE_DONE = 101;
        private static readonly IntPtr Transient = new IntPtr(-1);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_initialize();

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
                                               IntPtr cb, IntPtr cbArg, out IntPtr errMsg);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
                                                     int nByte, out IntPtr stmt, IntPtr tail);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_reset(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_text(IntPtr stmt, int idx,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string val, int n, IntPtr xDel);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_int64(IntPtr stmt, int idx, long val);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_bind_null(IntPtr stmt, int idx);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int iCol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int iCol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int iCol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_count(IntPtr stmt);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_name(IntPtr stmt, int iCol);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_errmsg(IntPtr db);

        [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
        private static extern void sqlite3_free(IntPtr ptr);

        private static string FromUtf8(IntPtr p, int len)
        {
            if (p == IntPtr.Zero || len <= 0) return "";
            byte[] buf = new byte[len];
            Marshal.Copy(p, buf, 0, len);
            return Encoding.UTF8.GetString(buf);
        }

        private static int StrLen(IntPtr p)
        {
            if (p == IntPtr.Zero) return 0;
            int i = 0;
            while (Marshal.ReadByte(p, i) != 0) i++;
            return i;
        }

        /// <summary>连接级错误（sqlite3_errmsg 返回的字符串由 sqlite 拥有，<strong>不要</strong> sqlite3_free）。</summary>
        private static string ConnErr(IntPtr db)
        {
            IntPtr p = sqlite3_errmsg(db);
            return FromUtf8(p, StrLen(p));
        }

        /// <summary>sqlite3_exec 的 out 参数分配的错误串，调用方需 sqlite3_free。</summary>
        private static string TakeErr(IntPtr err)
        {
            if (err == IntPtr.Zero) return "";
            string s = FromUtf8(err, StrLen(err));
            sqlite3_free(err);
            return s;
        }

        /// <summary>SQLite 操作失败异常。</summary>
        internal sealed class SqliteException : Exception
        {
            public SqliteException(string message) : base(message) { }
        }

        /// <summary>
        /// 显式初始化 SQLite 运行时，<strong>必须在首次 sqlite3_open 之前调用</strong>。
        /// <para>【必调，勿删】本机使用的这份官方 x64 构建，如果让 <c>sqlite3_open</c> 去
        /// <em>惰性自动初始化</em>，在 .NET 宿主的原生调用路径里会触发访问违例：进程直接段错误，
        /// 连托管的 try/catch 都拦不住（实测 sqlite3_open 一步崩溃，而先显式 init 后一切正常）。
        /// 因此启动时先显式调一次本方法。幂等，可重复调用（SQLite 内部有引用计数）。</para>
        /// </summary>
        internal static void EnsureInitialized()
        {
            int rc = sqlite3_initialize();
            if (rc != SQLITE_OK) throw new SqliteException("SQLite 初始化失败 rc=" + rc);
        }

        /// <summary>一个 SQLite 连接（封装 sqlite3* 句柄）。</summary>
        internal sealed class Connection : IDisposable
        {
            private IntPtr _db = IntPtr.Zero;
            private bool _open;

            public void Open(string path)
            {
                int rc = sqlite3_open(path ?? "", out _db);
                if (rc != SQLITE_OK) throw new SqliteException("打开数据库失败 rc=" + rc);
                _open = true;
            }

            /// <summary>执行无结果集的 SQL（建表 / 改 / 删），出错抛 <see cref="SqliteException"/>。</summary>
            public void Execute(string sql)
            {
                IntPtr err;
                int rc = sqlite3_exec(_db, sql, IntPtr.Zero, IntPtr.Zero, out err);
                if (rc != SQLITE_OK)
                    throw new SqliteException("执行失败：" + (err != IntPtr.Zero ? TakeErr(err) : ("rc=" + rc)));
            }

            public Statement Prepare(string sql)
            {
                IntPtr stmt;
                int rc = sqlite3_prepare_v2(_db, sql, -1, out stmt, IntPtr.Zero);
                if (rc != SQLITE_OK) throw new SqliteException("预处理失败：" + ConnErr(_db));
                return new Statement(stmt);
            }

            public void Close()
            {
                if (_open && _db != IntPtr.Zero)
                {
                    sqlite3_close(_db);
                    _open = false;
                    _db = IntPtr.Zero;
                }
            }

            public void Dispose() { Close(); }
        }

        /// <summary>一条已预处理的语句（封装 sqlite3_stmt*），用完 Dispose。</summary>
        internal sealed class Statement : IDisposable
        {
            private IntPtr _stmt;

            internal Statement(IntPtr stmt) { _stmt = stmt; }

            /// <summary>绑定文本（n=-1 让 SQLite 按 null 结尾算长度；Transient 表示内部复制）。</summary>
            public void BindText(int idx, string val)
            {
                int rc = sqlite3_bind_text(_stmt, idx, val ?? "", -1, Transient);
                if (rc != SQLITE_OK) throw new SqliteException("绑定文本失败 rc=" + rc);
            }

            public void BindInt64(int idx, long val)
            {
                int rc = sqlite3_bind_int64(_stmt, idx, val);
                if (rc != SQLITE_OK) throw new SqliteException("绑定整数失败 rc=" + rc);
            }

            public void BindNull(int idx)
            {
                int rc = sqlite3_bind_null(_stmt, idx);
                if (rc != SQLITE_OK) throw new SqliteException("绑定 NULL 失败 rc=" + rc);
            }

            /// <summary>执行一步；返回 true 表示拿到一行（ROW），false 表示结束（DONE）。</summary>
            public bool Step()
            {
                int rc = sqlite3_step(_stmt);
                if (rc == SQLITE_ROW) return true;
                if (rc == SQLITE_DONE) return false;
                throw new SqliteException("执行失败 rc=" + rc);
            }

            public void Reset() { sqlite3_reset(_stmt); }

            public int ColumnCount { get { return sqlite3_column_count(_stmt); } }

            public string ColumnName(int i) { return FromUtf8(sqlite3_column_name(_stmt, i), StrLen(sqlite3_column_name(_stmt, i))); }

            public string ColumnText(int i) { return FromUtf8(sqlite3_column_text(_stmt, i), sqlite3_column_bytes(_stmt, i)); }

            public long ColumnInt64(int i) { return sqlite3_column_int64(_stmt, i); }

            public void Dispose()
            {
                if (_stmt != IntPtr.Zero)
                {
                    sqlite3_finalize(_stmt);
                    _stmt = IntPtr.Zero;
                }
            }
        }
    }
}
