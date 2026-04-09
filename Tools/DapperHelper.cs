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
