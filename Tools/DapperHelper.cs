using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Dapper;

namespace WebApi.Tools
{


    //var sql = "SELECT * FROM YourTable WHERE Id = @Id";
    //var parameters = new { Id = 1 };
    //var result = connection.Query<YourEntityType>(sql, parameters).SingleOrDefault();

    public class DapperHelper : IDisposable
    {
        private readonly string _connectionString;
        private IDbConnection _connection;

        // 构造函数，注入数据库连接字符串
        public DapperHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        // 获取数据库连接
        private IDbConnection GetConnection()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new SqlConnection(_connectionString);
                _connection.Open();
            }
            return _connection;
        }

        // 执行查询，返回数据列表
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object param = null)
        {
            using (var connection = GetConnection())
            {
                return await connection.QueryAsync<T>(sql, param);
            }
        }

        // 执行查询，返回单个数据
        public async Task<T> QuerySingleAsync<T>(string sql, object param = null)
        {
            using (var connection = GetConnection())
            {
                return await connection.QuerySingleOrDefaultAsync<T>(sql, param);
            }
        }

        // 执行命令，返回受影响的行数
        public async Task<int> ExecuteAsync(string sql, object param = null)
        {
            using (var connection = GetConnection())
            {
                return await connection.ExecuteAsync(sql, param);
            }
        }
        /// <summary>
        /// 获取DataTable
        /// </summary>
        /// <param name="sql"></param>
        /// <param name="param"></param>
        /// <param name="commandType"></param>
        /// <returns></returns>
        public async Task<DataTable> GetDataTableAsync(string sql, object param = null, CommandType commandType = CommandType.Text)
        {
            using (var connection = new SqlConnection(_connectionString))  // 使用 SqlConnection
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
                    using (var reader = await cmd.ExecuteReaderAsync())  // 异步执行查询
                    {
                        dataTable.Load(reader);
                    }

                    return dataTable;
                }
            }
        }


        // 实现 IDisposable 接口，释放数据库连接
        public void Dispose()
        {
            if (_connection != null && _connection.State == ConnectionState.Open)
            {
                _connection.Close();
                _connection.Dispose();
            }
        }
    }
}
