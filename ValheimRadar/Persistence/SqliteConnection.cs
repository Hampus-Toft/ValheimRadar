using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ValheimRadar
{
    // Thin, exception-throwing wrapper over SqliteNative: one open database handle plus prepared
    // statements. Not thread-safe - PinManager only ever uses it from Unity's main thread.
    internal sealed class SqliteConnection : IDisposable
    {
        private IntPtr db;

        private SqliteConnection(IntPtr db)
        {
            this.db = db;
        }

        internal static SqliteConnection Open(string path)
        {
            int rc = SqliteNative.sqlite3_open_v2(Utf8(path), out IntPtr handle, SqliteNative.SQLITE_OPEN_READWRITE | SqliteNative.SQLITE_OPEN_CREATE, IntPtr.Zero);
            if (rc != SqliteNative.SQLITE_OK)
            {
                string message = handle != IntPtr.Zero ? ReadUtf8(SqliteNative.sqlite3_errmsg(handle)) : $"error {rc}";
                if (handle != IntPtr.Zero) SqliteNative.sqlite3_close_v2(handle);
                throw new InvalidOperationException($"Couldn't open SQLite database '{path}': {message}");
            }

            return new SqliteConnection(handle);
        }

        internal static string LibraryVersion => ReadUtf8(SqliteNative.sqlite3_libversion());

        // Runs a single statement (prepare_v2 ignores anything after the first ';'), discarding rows.
        internal void Execute(string sql)
        {
            using (var statement = Prepare(sql)) statement.StepToEnd();
        }

        internal SqliteStatement Prepare(string sql)
        {
            byte[] bytes = Utf8(sql);
            int rc = SqliteNative.sqlite3_prepare_v2(db, bytes, bytes.Length, out IntPtr stmt, IntPtr.Zero);
            if (rc != SqliteNative.SQLITE_OK) throw Error($"prepare failed for \"{sql}\"");
            return new SqliteStatement(this, stmt);
        }

        // Runs action inside BEGIN/COMMIT, rolling back if it throws.
        internal void InTransaction(Action action)
        {
            Execute("BEGIN IMMEDIATE");
            try
            {
                action();
                Execute("COMMIT");
            }
            catch
            {
                try { Execute("ROLLBACK"); } catch { /* keep the original error */ }
                throw;
            }
        }

        internal InvalidOperationException Error(string context) =>
            new InvalidOperationException($"SQLite {context}: {ReadUtf8(SqliteNative.sqlite3_errmsg(db))}");

        public void Dispose()
        {
            if (db == IntPtr.Zero) return;
            SqliteNative.sqlite3_close_v2(db);
            db = IntPtr.Zero;
        }

        // NUL-terminated, as sqlite3_open_v2/prepare_v2 expect.
        internal static byte[] Utf8(string value)
        {
            int length = Encoding.UTF8.GetByteCount(value);
            byte[] bytes = new byte[length + 1];
            Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
            return bytes;
        }

        internal static string ReadUtf8(IntPtr ptr, int length = -1)
        {
            if (ptr == IntPtr.Zero) return null;
            if (length < 0)
            {
                length = 0;
                while (Marshal.ReadByte(ptr, length) != 0) length++;
            }

            byte[] bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    internal sealed class SqliteStatement : IDisposable
    {
        private readonly SqliteConnection connection;
        private IntPtr stmt;

        internal SqliteStatement(SqliteConnection connection, IntPtr stmt)
        {
            this.connection = connection;
            this.stmt = stmt;
        }

        // Parameters are 1-based, in the order of the ?s in the SQL.
        internal SqliteStatement Bind(int index, long value) => Check(SqliteNative.sqlite3_bind_int64(stmt, index, value));
        internal SqliteStatement Bind(int index, double value) => Check(SqliteNative.sqlite3_bind_double(stmt, index, value));

        internal SqliteStatement Bind(int index, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            return Check(SqliteNative.sqlite3_bind_text(stmt, index, bytes, bytes.Length, SqliteNative.SQLITE_TRANSIENT));
        }

        // True while a row is available; false once the statement is done.
        internal bool Step()
        {
            int rc = SqliteNative.sqlite3_step(stmt);
            if (rc == SqliteNative.SQLITE_ROW) return true;
            if (rc == SqliteNative.SQLITE_DONE) return false;
            throw connection.Error("step failed");
        }

        internal void StepToEnd()
        {
            while (Step()) { }
        }

        // Runs the statement once with the current bindings, then makes it ready for the next set.
        internal void ExecuteAndReset()
        {
            StepToEnd();
            Reset();
        }

        // Makes the statement ready to run again with new bindings.
        internal void Reset()
        {
            SqliteNative.sqlite3_reset(stmt);
            SqliteNative.sqlite3_clear_bindings(stmt);
        }

        internal long GetInt64(int column) => SqliteNative.sqlite3_column_int64(stmt, column);
        internal double GetDouble(int column) => SqliteNative.sqlite3_column_double(stmt, column);

        internal string GetText(int column)
        {
            if (SqliteNative.sqlite3_column_type(stmt, column) == SqliteNative.SQLITE_NULL) return string.Empty;
            IntPtr text = SqliteNative.sqlite3_column_text(stmt, column);
            return SqliteConnection.ReadUtf8(text, SqliteNative.sqlite3_column_bytes(stmt, column)) ?? string.Empty;
        }

        public void Dispose()
        {
            if (stmt == IntPtr.Zero) return;
            SqliteNative.sqlite3_finalize(stmt);
            stmt = IntPtr.Zero;
        }

        private SqliteStatement Check(int rc)
        {
            if (rc != SqliteNative.SQLITE_OK) throw connection.Error("bind failed");
            return this;
        }
    }
}
