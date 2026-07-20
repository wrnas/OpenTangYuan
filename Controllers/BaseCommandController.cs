using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using TangYuan.Models;

namespace TangYuan.Controllers
{
    /// <summary>
    /// Base controller with multi-database support, providing common Dapper operations and transaction management.
    /// </summary>
    public abstract class BaseCommandController : Controller, IDisposable
    {
        protected readonly IConfiguration _config;
        protected readonly ILogger<BaseCommandController> _logger;

        private DbConnection? _currentConnection;
        private DbTransaction? _currentTransaction;
        protected bool HasActiveTransaction => _currentTransaction != null;

        protected BaseCommandController(IConfiguration configuration, ILogger<BaseCommandController> logger)
        {
            _config = configuration;
            _logger = logger;
        }

        // ==============================
        // Overridable connection creation strategy
        // (Can be customized by derived controllers)
        // ==============================

        /// <summary>
        /// Creates a database connection based on the configuration.
        /// By default, the first non-empty configuration is selected in the following order:
        /// Sqlite -> MySql.
        /// </summary>
        protected virtual DbConnection GetDbConnection()
        {
            // 1.  SQLite
            var sqliteConn = _config.GetConnectionString("Sqlite");
            if (!string.IsNullOrEmpty(sqliteConn))
            {
                var resolvedConn = ResolveSqliteConnectionString(sqliteConn);
                return new SqliteConnection(resolvedConn);
            }

            // 2.  MySQL
            var mysqlConn = _config.GetConnectionString("MySql");
            if (!string.IsNullOrEmpty(mysqlConn))
            {
                return new MySqlConnection(mysqlConn);
            }

            //throw new InvalidOperationException("未配置任何数据库连接字符串，请检查 appsettings.json 中的 ConnectionStrings 节点（Sqlite/MySql）");
            throw new InvalidOperationException(
    "No database connection string is configured. Please check the ConnectionStrings section (Sqlite/MySql) in appsettings.json."
);

        }

        /// <summary>
        /// Converts relative paths in a SQLite connection string to absolute paths based on the application's base directory.
        /// </summary>

        private string ResolveSqliteConnectionString(string connectionString)
        {
            // Matches the Data Source=xxx; portion, ignoring case and whitespace
            var pattern = @"Data\s*Source\s*=\s*(?<path>[^;]+)";
            var match = Regex.Match(connectionString, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return connectionString;

            var originalPath = match.Groups["path"].Value.Trim();
            if (string.IsNullOrEmpty(originalPath))
                return connectionString;

            // If the path is absolute, such as a Windows drive path or a Linux root path, use it as-is
            if (Path.IsPathRooted(originalPath))
                return connectionString;

            // Convert the relative path to an absolute path based on the application's base directory
            var absolutePath = Path.Combine(AppContext.BaseDirectory, originalPath);

            // Replace the path portion in the original connection string
            var newConnString = Regex.Replace(
                connectionString,
                pattern,
                $"Data Source={absolutePath}",
                RegexOptions.IgnoreCase);

            return newConnString;
        }



        // ==================== Transaction Control (Enhanced Resource Management) ====================
        protected void BeginTransaction()
        {
            if (_currentConnection != null)
                throw new InvalidOperationException("An active transaction already exists. Commit or roll it back first.");

            var connection = GetDbConnection();
            try
            {
                connection.Open();
                _currentTransaction = connection.BeginTransaction();
                _currentConnection = connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        protected void CommitTransaction()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("No active transaction exists.");

            try
            {
                _currentTransaction.Commit();
            }
            finally
            {
                CleanupTransaction();
            }
        }

        protected void RollbackTransaction()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("No active transaction exists.");

            try
            {
                _currentTransaction.Rollback();
            }
            finally
            {
                CleanupTransaction();
            }
        }

        private void CleanupTransaction()
        {
            _currentTransaction?.Dispose();
            _currentConnection?.Dispose();
            _currentTransaction = null;
            _currentConnection = null;
        }

        // ==================== 确保资源释放 ====================
        public void Dispose()
        {
            if (_currentTransaction != null)
            {
                // 未提交的事务自动回滚
                try { _currentTransaction.Rollback(); } catch { /* 忽略回滚异常 */ }
                _currentTransaction.Dispose();
            }
            _currentConnection?.Dispose();
        }

        // ==================== Connection Availability Check (Optional) ====================

        /// <summary>
        /// Ensures that the connection associated with the current transaction remains open.
        /// If it is closed, an attempt is made to reopen it.
        /// This method usually does not need to be called manually.
        /// </summary>
        protected void EnsureConnectionOpen()
        {
            if (_currentConnection != null && _currentConnection.State != ConnectionState.Open)
            {
                _currentConnection.Open();
            }
        }

        // ==================== Common Dapper Methods (Asynchronous, with ConfigureAwait) ====================
        protected async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                EnsureConnectionOpen();
                return await _currentConnection!.QueryFirstOrDefaultAsync<T>(sql, param, _currentTransaction, commandType: commandType)
                                                  .ConfigureAwait(false);
            }
            using var conn = GetDbConnection();
            return await conn.QueryFirstOrDefaultAsync<T>(sql, param, commandType: commandType)
                              .ConfigureAwait(false);
        }

        protected async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                EnsureConnectionOpen();
                return await _currentConnection!.QueryAsync<T>(sql, param, _currentTransaction, commandType: commandType)
                                                .ConfigureAwait(false);
            }
            using var conn = GetDbConnection();
            return await conn.QueryAsync<T>(sql, param, commandType: commandType)
                              .ConfigureAwait(false);
        }

        protected async Task<int> ExecuteAsync(string sql, object? param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                EnsureConnectionOpen();
                return await _currentConnection!.ExecuteAsync(sql, param, _currentTransaction, commandType: commandType)
                                                .ConfigureAwait(false);
            }
            using var conn = GetDbConnection();
            return await conn.ExecuteAsync(sql, param, commandType: commandType)
                              .ConfigureAwait(false);
        }

        protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                EnsureConnectionOpen();
                return await _currentConnection!.ExecuteScalarAsync<T>(sql, param, _currentTransaction, commandType: commandType)
                                                .ConfigureAwait(false);
            }
            using var conn = GetDbConnection();
            return await conn.ExecuteScalarAsync<T>(sql, param, commandType: commandType)
                              .ConfigureAwait(false);
        }

        // ==================== 快捷返回（可根据实际 ResponseHelper 调整） ====================
        /// <summary>
        /// 返回错误响应，假设存在 ResponseHelper 类（请根据实际情况引入或修改）
        /// </summary>
        protected IActionResult HandleError(string message)
        {
            _logger.LogError(message);
            return BadRequest(ResponseHelper.Fail<object>(message));  // 确保 ResponseHelper 可用
        }

        /// <summary>
        /// 返回成功响应
        /// </summary>
        protected IActionResult HandleSuccess(object? result = null, string message = "ok")
        {
            return Ok(ResponseHelper.Success(result, message));       // 确保 ResponseHelper 可用
        }
    }
}