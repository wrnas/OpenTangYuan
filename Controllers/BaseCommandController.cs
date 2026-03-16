using TangYuan.Models;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace TangYuan.Controllers
{
    public class BaseCommandController : Controller
    {
        protected readonly IConfiguration _config;
        protected readonly ILogger<BaseCommandController> _logger;

        // 事务相关字段
        private IDbConnection _currentConnection;
        private IDbTransaction _currentTransaction;
        protected bool HasActiveTransaction => _currentTransaction != null;

        public BaseCommandController(IConfiguration configuration, ILogger<BaseCommandController> logger)
        {
            _config = configuration;
            _logger = logger;
        }

        protected string Constring_MsSql => _config["ConnectionStrings:MsSql"];

        protected string GetCustomSetting(string key)
        {
            return _config[key];
        }

        protected IActionResult HandleError(string message)
        {
            _logger.LogError(message);
            return BadRequest(ResponseHelper.Fail<object>(message));
        }

        protected IActionResult HandleSuccess(object result, string message = "ok")
        {
            return Ok(ResponseHelper.Success(result, message));
        }

        protected IDbConnection GetMsSqlConnection()
        {
            return new SqlConnection(Constring_MsSql);
        }

        // ==================== 事务控制方法 ====================
        protected void BeginTransaction()
        {
            if (_currentConnection != null)
                throw new InvalidOperationException("已有活动事务，请先提交或回滚");
            _currentConnection = GetMsSqlConnection();
            _currentConnection.Open();
            _currentTransaction = _currentConnection.BeginTransaction();
        }

        protected void CommitTransaction()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("没有活动事务");
            _currentTransaction.Commit();
            _currentTransaction.Dispose();
            _currentConnection?.Dispose();
            _currentTransaction = null;
            _currentConnection = null;
        }

        protected void RollbackTransaction()
        {
            if (_currentTransaction == null)
                throw new InvalidOperationException("没有活动事务");
            _currentTransaction.Rollback();
            _currentTransaction.Dispose();
            _currentConnection?.Dispose();
            _currentTransaction = null;
            _currentConnection = null;
        }

        // ==================== Dapper 辅助方法（自动感知事务，使用命名参数避免歧义） ====================

        protected async Task<T> QueryFirstOrDefaultAsync<T>(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.QueryFirstOrDefaultAsync<T>(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                using (var connection = GetMsSqlConnection())
                {
                    return await connection.QueryFirstOrDefaultAsync<T>(
                        sql: sql,
                        param: param,
                        commandType: commandType);
                }
            }
        }

        protected async Task<IEnumerable<T>> QueryAsync<T>(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.QueryAsync<T>(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                using (var connection = GetMsSqlConnection())
                {
                    return await connection.QueryAsync<T>(
                        sql: sql,
                        param: param,
                        commandType: commandType);
                }
            }
        }

        protected async Task<T> QuerySingleAsync<T>(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.QuerySingleOrDefaultAsync<T>(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                using (var connection = GetMsSqlConnection())
                {
                    return await connection.QuerySingleOrDefaultAsync<T>(
                        sql: sql,
                        param: param,
                        commandType: commandType);
                }
            }
        }

        protected async Task<int> ExecuteAsync(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.ExecuteAsync(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                using (var connection = GetMsSqlConnection())
                {
                    return await connection.ExecuteAsync(
                        sql: sql,
                        param: param,
                        commandType: commandType);
                }
            }
        }

        protected async Task<T> ExecuteScalarAsync<T>(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.ExecuteScalarAsync<T>(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                using (var connection = GetMsSqlConnection())
                {
                    return await connection.ExecuteScalarAsync<T>(
                        sql: sql,
                        param: param,
                        commandType: commandType);
                }
            }
        }

        protected async Task<IDataReader> GetDataReaderByDapperAsync(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            if (HasActiveTransaction)
            {
                return await _currentConnection.ExecuteReaderAsync(
                    sql: sql,
                    param: param,
                    transaction: _currentTransaction,
                    commandType: commandType);
            }
            else
            {
                // 注意：此处不能使用 using，因为需要返回 reader 供外部使用
                var connection = GetMsSqlConnection();
                // 需要手动打开连接
                connection.Open();
                return await connection.ExecuteReaderAsync(
                    sql: sql,
                    param: param,
                    commandType: commandType);
                // 调用者必须负责关闭 reader 和 connection
            }
        }

        // ==================== 以下为非 Dapper 方法（不支持事务，请勿在事务块中使用） ====================

        protected async Task<DataTable> GetDataTableAsync(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            using (var connection = new SqlConnection(Constring_MsSql))
            {
                await connection.OpenAsync();

                using (var cmd = new SqlCommand(sql, connection))
                {
                    cmd.CommandType = commandType;

                    if (param != null)
                    {
                        foreach (var property in param.GetType().GetProperties())
                        {
                            var value = property.GetValue(param);
                            var parameter = new SqlParameter(property.Name, value ?? DBNull.Value);
                            cmd.Parameters.Add(parameter);
                        }
                    }

                    var dataTable = new DataTable();
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        dataTable.Load(reader);
                    }

                    return dataTable;
                }
            }
        }

        protected async Task<SqlDataReader> GetDataReaderAsync(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            var connection = new SqlConnection(Constring_MsSql);
            await connection.OpenAsync();

            using (var cmd = new SqlCommand(sql, connection))
            {
                cmd.CommandType = commandType;

                if (param != null)
                {
                    foreach (var property in param.GetType().GetProperties())
                    {
                        var value = property.GetValue(param);
                        var parameter = new SqlParameter(property.Name, value ?? DBNull.Value);
                        cmd.Parameters.Add(parameter);
                    }
                }

                var reader = await cmd.ExecuteReaderAsync();
                return reader;
                // 注意：调用者必须负责关闭 reader 和 connection
            }
        }
    }
}