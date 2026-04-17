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
    /// 支持多数据库的控制器基类，封装 Dapper 基本操作和事务管理
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
        // 可重写的连接创建策略（子类可自定义）
        // ==============================
        /// <summary>
        /// 根据配置获取数据库连接，默认按 Sqlite -> MySql顺序取第一个非空配置
        /// </summary>
        protected virtual DbConnection GetDbConnection()
        {
            // 1. 优先 SQLite
            var sqliteConn = _config.GetConnectionString("Sqlite");
            if (!string.IsNullOrEmpty(sqliteConn))
            {
                var resolvedConn = ResolveSqliteConnectionString(sqliteConn);
                return new SqliteConnection(resolvedConn);
            }

            // 2. 其次 MySQL
            var mysqlConn = _config.GetConnectionString("MySql");
            if (!string.IsNullOrEmpty(mysqlConn))
            {
                return new MySqlConnection(mysqlConn);
            }

            throw new InvalidOperationException("未配置任何数据库连接字符串，请检查 appsettings.json 中的 ConnectionStrings 节点（Sqlite/MySql）");
        }

        /// <summary>
        /// 将 SQLite 连接字符串中的相对路径转换为基于程序基目录的绝对路径
        /// </summary>
        private string ResolveSqliteConnectionString(string connectionString)
        {
            // 匹配 Data Source=xxx; 部分（忽略大小写和空格）
            var pattern = @"Data\s*Source\s*=\s*(?<path>[^;]+)";
            var match = Regex.Match(connectionString, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                return connectionString;

            var originalPath = match.Groups["path"].Value.Trim();
            if (string.IsNullOrEmpty(originalPath))
                return connectionString;

            // 如果是绝对路径（Windows 盘符或 Linux 根路径），则直接使用
            if (Path.IsPathRooted(originalPath))
                return connectionString;

            // 相对路径 → 转换为程序所在目录的绝对路径
            var absolutePath = Path.Combine(AppContext.BaseDirectory, originalPath);
            // 替换原字符串中的路径部分
            var newConnString = Regex.Replace(connectionString, pattern, $"Data Source={absolutePath}", RegexOptions.IgnoreCase);
            return newConnString;
        }

        // ==================== 事务控制（增强资源管理） ====================
        protected void BeginTransaction()
        {
            if (_currentConnection != null)
                throw new InvalidOperationException("已有活动事务，请先提交或回滚");

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
                throw new InvalidOperationException("没有活动事务");

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
                throw new InvalidOperationException("没有活动事务");

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

        // ==================== 检查连接可用性（可选） ====================
        /// <summary>
        /// 确保事务中的连接仍然打开，若关闭则尝试重新打开（通常不需要手动调用）
        /// </summary>
        protected void EnsureConnectionOpen()
        {
            if (_currentConnection != null && _currentConnection.State != ConnectionState.Open)
            {
                _currentConnection.Open();
            }
        }

        // ==================== Dapper 通用方法（异步，带 ConfigureAwait） ====================
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