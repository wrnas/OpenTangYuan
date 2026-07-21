using System;
using System.Collections;
using System.Data;
using System.Data.Common;
//using MySql.Data.MySqlClient;
//using MySql.Data;
using System.Reflection;
//using Microsoft.Data.Sqlite;
//using Oracle.ManagedDataAccess.Client;
using System.Collections.Generic;
using System.Data.SqlClient;

namespace I5iw.Lib
{
    #region 数据访问助手类  For NET2.0
    /// <summary>
    /// 数据访问助手类 For NET2.0 by PHF
    /// 本类支持常见的数据库类型。支持范围参见
    /// DbHelper.DataProviderType
    /// 一个填充数据集的例子
    /// </summary>
    /// <example> 一个填充数据集的例子.
    /// <code>
    ///private void button1_Click(object sender, EventArgs e)
    ///    {
    ///        string connStr = "server=127.0.0.1; user id=sa; pwd=;database=pubs";
    ///        //DbHelper dbHelper = new DbHelper(connStr);一个参数的构造函数
    ///        DbHelper dbHelper = new DbHelper(DbHelper.DataProviderType.SqlServer, connStr);
    ///        ds = dbHelper.GetDataSetWithSql(dbHelper.ConnString, "SELECT * FROM titles", null);
    ///        dataGridView1.DataSource = ds.Tables[0];
    ///    }
    /// </code>
    /// </example>
    /// <remarks>
    /// 备注
    /// </remarks>

    public class DbHelper
    {
        /// <summary>
        /// 全局连接器
        /// </summary>
        DbConnection G_connection = null;        

        public DbConnection Connection
        {
            get { return G_connection; }
        }

        /// <summary>
        /// 全局命令行
        /// </summary>
        DbCommand G_command = null;
        /// <summary>
        /// 全局命令构造器
        /// </summary>
        DbCommandBuilder G_commandBuilder = null;
        /// <summary>
        /// 全局数据适配器
        /// </summary>
        DbDataAdapter G_dataAdapter = null;

        private string connString = "server=127.0.0.1; user id=sa; pwd=;database=pubs";
        
        private string errorText = string.Empty;
        /// <summary>
        /// 错误信息属性
        /// </summary>
        public string ErrorText
        {
            get { return errorText; }            
        }

        /// <summary>
        /// 连接字符串
        /// </summary>
        public string ConnString
        {
            get { return connString; }
            set { connString = value; }
        }
        #region 私有变量
        /// <summary>
        /// DBHelper支持的数据库类型集合
        /// </summary>
        public enum DataProviderType
        {
            /// <summary>
            /// sqlServer类型
            /// 这个就不用废话了
            /// </summary>
            SqlServer,
            /// <summary>
            /// access类型
            /// 这个就不用废话了
            /// </summary>
            Access,
            /// <summary>
            /// 适用于 Oracle 数据源
            /// 支持 Oracle 客户端软件 8.1.7 和更高版本
            /// </summary>
            Oracle,
            /// <summary>
            /// 提供对使用 ODBC 公开的数据源中数据的访问
            /// </summary>
            Odbc,
            /// <summary>
            /// 提供对使用 OLE DB 公开的数据源中数据的访问
            /// </summary>
            OleDb,
            /// <summary>
            /// 可以创建能部署在桌面计算机、
            /// 智能设备和 Tablet PC 上的压缩数据库
            /// 3.5版本
            /// </summary>
            SqlServerCe,
            /// <summary>
            /// MySql数据库
            /// </summary>
            MySql,
            /// <summary>
            /// IBM的Db2数据库
            /// </summary>
            DB2,
            /// <summary>
            /// SQLite
            /// </summary>
            SQLite
        }
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        protected string m_connectionstring = null;
        /// <summary>
        /// 数据库类型(.net可识别的类型)
        /// </summary>
        private string dbType = string.Empty;

        /// <summary>
        /// 数据库类型(.net可识别的类型)
        /// </summary>
        public string DbType
        {
            get { return dbType; }
        }

        /// <summary>
        /// DbProviderFactory实例
        /// </summary>
        private DbProviderFactory m_factory = null;


        /// <summary>
        /// 查询次数统计
        /// </summary>
        private int m_querycount = 0;
        /// <summary>
        /// Parameters缓存哈希表
        /// </summary>
        private Hashtable m_paramcache = Hashtable.Synchronized(new Hashtable());
        private object lockHelper = new object();

        #endregion

        #region 属性

        /// <summary>
        /// 查询次数统计
        /// </summary>
        public int QueryCount
        {
            get { return m_querycount; }
            set { m_querycount = value; }
        }

        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        public string ConnectionString
        {
            get
            {
                return m_connectionstring;
            }
            set
            {
                m_connectionstring = value;
            }
        }


        /// <summary>
        /// DbFactory实例
        /// </summary>
        public DbProviderFactory Factory
        {
            get { return m_factory; }
        }
        #endregion

        #region 构造函数
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="dataProviderType">数据库类型</param>
        /// <param name="connString">连接字符串</param>
        public DbHelper(DataProviderType dataProviderType, string connString)
        {
            List<string> dbP = (List<string>)DbProviderFactories.GetProviderInvariantNames();
            if (!dbP.Contains("System.Data.SqlClient"))
            {
                DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            }

            if (!dbP.Contains("System.Data.OleDb"))
            {
                DbProviderFactories.RegisterFactory("System.Data.OleDb", System.Data.SqlClient.SqlClientFactory.Instance);
            }

            dbType = dataProviderType.ToString();
            string ole_str = string.Empty;
            switch (dataProviderType)
            {
                case DataProviderType.SqlServer:
                    {   
                        m_factory = DbProviderFactories.GetFactory("System.Data.SqlClientt");
                        break;
                    }
                case DataProviderType.Access:
                    {
                        m_factory = DbProviderFactories.GetFactory("System.Data.OleDb");
                        break;
                    }
                case DataProviderType.Oracle:
                    {
                        //try
                        //{   
                        //    //使用最新方法
                        //    this.connString = connString;
                        //    this.ConnString = connString;
                        //    ConnectionString = connString;
                        //    G_connection = new Oracle.ManagedDataAccess.Client.OracleConnection(connString);
                        //    G_connection.Open();
                        //    G_command = new Oracle.ManagedDataAccess.Client.OracleCommand();
                        //    G_commandBuilder = new Oracle.ManagedDataAccess.Client.OracleCommandBuilder();
                        //    G_dataAdapter = new Oracle.ManagedDataAccess.Client.OracleDataAdapter();
                        //    return;
                        //}
                        //catch (Exception exp)
                        //{
                        //    errorText = exp.Message;
                        //}
                        break;
                    }
                case DataProviderType.Odbc:
                    {
                        m_factory = DbProviderFactories.GetFactory("System.Data.Odbc");
                        break;
                    }
                case DataProviderType.OleDb:
                    {
                        m_factory = DbProviderFactories.GetFactory("System.Data.OleDb");
                        break;
                    }
                case DataProviderType.SqlServerCe:
                    {
                        m_factory = DbProviderFactories.GetFactory("System.Data.SqlServerCe.3.5");
                        break;
                    }
                case DataProviderType.SQLite:
                    {
                        //这里没测试。
                        m_factory = DbProviderFactories.GetFactory("Microsoft.Data.Sqlite");
                        break;
                    }
                case DataProviderType.MySql:
                    {
                        //由于未知原因，所以不用再通过  getConnection 获取连接。                        
                        //m_factory = DbProviderFactories.GetFactory("MySql.Data.MySqlClient");
                        //break;
                        //this.connString = connString;
                        //this.ConnString = connString;
                        //ConnectionString = connString;

                        //G_connection = new MySql.Data.MySqlClient.MySqlConnection(ConnString);
                        //G_connection.Open();
                        //G_command = new MySqlCommand();
                        //G_commandBuilder = new MySqlCommandBuilder();
                        //G_dataAdapter = new MySqlDataAdapter();                        

                        return;
                    }

                default:
                    m_factory = DbProviderFactories.GetFactory("System.Data.SqlClient");
                    break;
            }

            if (connString.Trim() != string.Empty)
            {
                ConnectionString = connString;
                ConnString = connString;
            }           

        }

        /// <summary>
        /// 构造函数(默认使用SqlServer，想更改驱动类型请使用含有两个参数的构造函数
        /// public DbHelper(DataProviderType dataProviderType, string connString))
        /// </summary>
        /// <param name="connString">连接字符串</param>
        public DbHelper(string connString)
        {
            dbType = "SqlServer";

            List<string> dbP = (List<string>)DbProviderFactories.GetProviderInvariantNames();
            if (!dbP.Contains("System.Data.SqlClient"))
            {
                DbProviderFactories.RegisterFactory("System.Data.SqlClient", System.Data.SqlClient.SqlClientFactory.Instance);
            }

            if (!dbP.Contains("System.Data.OleDb"))
            {
                DbProviderFactories.RegisterFactory("System.Data.OleDb", System.Data.SqlClient.SqlClientFactory.Instance);
            }

            m_factory = DbProviderFactories.GetFactory("System.Data.SqlClient");
            if (connString.Trim() != string.Empty)
                if (connString.Trim() != string.Empty)
                {
                    ConnectionString = connString;
                    ConnString = connString;
                }
            //如果SqlClient方式无法连接，则转入 oledb方式。
            if (getConnection() == null)
            {
                m_factory = DbProviderFactories.GetFactory("System.Data.OleDb");
                if (connString.Trim() != string.Empty)
                    if (connString.Trim() != string.Empty)
                    {
                        ConnectionString = connString;
                        ConnString = connString;
                        G_connection = Factory.CreateConnection();
                        G_connection.ConnectionString = connString;
                        G_command = Factory.CreateCommand();
                        G_commandBuilder = Factory.CreateCommandBuilder();
                        G_dataAdapter = Factory.CreateDataAdapter();
                    }
            }
            //开始根据字符串判断数据库类型。仅为有限枚举。
        }
        #endregion

        #region  数据库操作方法(全静态)  目前全部测试通过 phf
        //------------------------------------------------------------------------------
        // 存储参数的哈希表（暂时不用）
        private Hashtable parmCache = Hashtable.Synchronized(new Hashtable());

        #region  返回数据库连接对象
        /// <summary>
        /// 数据库连接对象
        /// </summary>
        /// <returns></returns>
        public DbConnection getConnection()
        {
            errorText = string.Empty;
            try
            {                
                if ((G_connection == null)&&(m_factory!=null))
                {
                    G_connection = Factory.CreateConnection();
                    G_connection.ConnectionString = connString;
                    G_command = Factory.CreateCommand();
                    G_commandBuilder = Factory.CreateCommandBuilder();
                    G_dataAdapter = Factory.CreateDataAdapter();
                }
                return G_connection;
            }
            catch (Exception exp)
            {
                //return null;                
                errorText = exp.Message;
                return null; 
            }

        }
        #endregion

        #region 测试是否能够连接
        /// <summary>
        /// 数据库是否可以正常连接
        /// </summary>
        /// <returns></returns>
        public bool IsConnection()
        {
            try
            {
                if (Connection.State == ConnectionState.Open)
                {                   
                    return true;
                }
                else
                {
                    Connection.Open();
                    if (Connection.State == ConnectionState.Open)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch(Exception exp)
            {
                errorText = exp.Message;
                return false;
            }
            finally
            {
                Connection.Close();
            }

        }


        #endregion

        #region 参数处理，分解参数列表
        #region 参数处理
        /// <summary>
        /// 对传入的参数列表进行预处理（DbConnection）
        /// </summary>
        /// <param name="cmd">命令执行对象</param>
        /// <param name="conn">数据库连接对象</param>
        /// <param name="trans">事务管理对象</param>
        /// <param name="cmdType">命令执行方式（sql执行还是存储过程）</param>
        /// <param name="procName">命令执行语句.例如 Select * from Products</param>
        /// <param name="cmdParms">命令参数</param>
        private void PrepareCommand(DbCommand cmd, DbConnection conn, DbTransaction trans, CommandType cmdType, string cmdText, DbParameter[] cmdParms)
        {

            if (conn != null)
            {
                if (conn.State != ConnectionState.Open)
                    conn.Open();

                cmd.Connection = conn;
            }

            cmd.CommandText = cmdText;

            if (trans != null)
                cmd.Transaction = trans;

            cmd.CommandType = cmdType;

            if (cmdParms != null)
            {
                foreach (DbParameter parm in cmdParms)
                    cmd.Parameters.Add(parm);
            }
           // cmd.Connection.Close();
        }
        #endregion

        #region 参数处理
        /// <summary>
        /// 对传入的参数进行处理(connString)
        /// </summary>
        /// <param name="cmd">DbCommand对象</param>
        /// <param name="connString">连接字符串</param>
        /// <param name="cmdType">命令类型</param>
        /// <param name="selectText">命令语句</param>
        /// <param name="cmdParms">参数列表</param>
        private void PrepareCommand(DbCommand cmd, string connectionString, CommandType cmdType, string cmdText, DbParameter[] cmdParms)
        {
            try
            {

                if (G_connection != null)
                {
                    G_connection.Close();
                    G_connection.ConnectionString = ConnectionString;
                    G_connection.Open();

                    this.G_command.Connection = G_connection;
                }

                G_command.CommandText = cmdText;

                G_command.CommandType = cmdType;

                if (cmdParms != null)
                {
                    foreach (DbParameter parm in cmdParms)
                        G_command.Parameters.Add(parm);
                }
            }
            catch (Exception)
            {

                this.G_connection.Close();
                //connection.Dispose();
            }
            finally
            {
                //cmd.Connection.Close();
            }


        }
        #endregion

        #region 参数处理
        /// <summary>
        /// 对传入的参数进行处理
        /// </summary>
        /// <param name="cmd">DbCommand对象</param>
        /// <param name="cmdType">命令类型</param>
        /// <param name="procName">sql语句</param>
        /// <param name="cmdParms">参数列表</param>
        private void PrepareCommand(DbCommand cmd, CommandType cmdType, string cmdText, DbParameter[] cmdParms)
        {

            cmd.CommandText = cmdText;

            cmd.CommandType = cmdType;

            if (cmdParms != null)
            {
                foreach (DbParameter parm in cmdParms)
                    cmd.Parameters.Add(parm);
            }
        }
        #endregion

        #region 参数处理
        /// <summary>
        /// 对传入的参数进行处理
        /// </summary>
        /// <param name="cmd">DbCommand对象</param>
        /// <param name="cmdType">命令类型</param>
        /// <param name="cmdParms">参数列表</param>
        private void PrepareCommand(DbCommand cmd, CommandType cmdType, DbParameter[] cmdParms)
        {

            cmd.CommandType = cmdType;

            if (cmdParms != null)
            {
                foreach (DbParameter parm in cmdParms)
                    cmd.Parameters.Add(parm);
            }
        }
        #endregion

        #endregion

        #region  执行一个无返回值的sql命令
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, "insert into .....", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connString">数据库链接字符串</param> 
        /// <param name="selectText">sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>      
        public int ExecuteNonQueryWithSql(string connectionString, string sqlText, params DbParameter[] commandParameters)
        {
            errorText = string.Empty;
            try
            {
                if (G_connection != null)
                {
                    G_connection.Close();
                }
                this.G_connection.ConnectionString = ConnectionString;
                PrepareCommand(G_command, G_connection, null, CommandType.Text, sqlText, commandParameters);
                int val = this.G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                G_connection.Close();
            }

        }
        #endregion

        #region  执行一个无返回值的过程
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connString">数据库链接字符串</param>
        /// <param name="commandType">命令执行类型(stored procedure, text, etc.)</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>      
        public int ExecuteNonQueryWithProc(string connectionString, string procName, params DbParameter[] commandParameters)
        {
            errorText = string.Empty;
            try
            {
                if (G_connection != null)
                {
                    G_connection.Close();
                }
                this.G_connection.ConnectionString = ConnectionString;
                PrepareCommand(this.G_command, G_connection, null, CommandType.StoredProcedure, procName, commandParameters);
                int val = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;

                return 0;
            }
            finally
            {
                G_connection.Close();
            }

        }
        #endregion

        #region  执行一个无返回值的命令或过程
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connString">数据库链接字符串</param>
        /// <param name="commandType">命令执行类型(stored procedure, text, etc.)</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>      
        public int ExecuteNonQuery(string connectionString, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {
            errorText = string.Empty;
            try
            {
                if (G_connection != null)
                {
                    G_connection.Close();
                }
                this.G_connection.ConnectionString = ConnectionString;
                this.G_connection.Open();
                PrepareCommand(this.G_command, G_connection, null, cmdType, cmdText, commandParameters);
                int resultCount = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return resultCount;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;

                return 0;
            }
            finally
            {
                this.G_connection.Close();
            }

        }
        #endregion

        #region 执行一个无返回值的Sql语句
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connection">数据库连接字符串</param>
        /// <param name="commandText">sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>
        public int ExecuteNonQueryWithSql(DbConnection connection, string sqlText, params DbParameter[] commandParameters)
        {
            DbTransaction DbTransaction = this.G_connection.BeginTransaction();
            this.G_command.Transaction = DbTransaction;
            PrepareCommand(G_command, G_connection, null, CommandType.Text, sqlText, commandParameters);
            errorText = string.Empty;
            try
            {
                int resultCount = G_command.ExecuteNonQuery();
                DbTransaction.Commit();
                G_command.Parameters.Clear();
                return resultCount;
            }
            catch (Exception exp)
            {
                DbTransaction.Rollback();
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                G_connection.Close();
            }
        }
        #endregion

        #region 执行一个有return返回值的存储过程
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connection">数据库连接字符串</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>
        public object ExecuteReturnQueryWithPro(DbConnection connection, string proName, params DbParameter[] commandParameters)
        {
            object result = null;
            try
            {                
                errorText = string.Empty;
                PrepareCommand(this.G_command, this.G_connection, null, CommandType.StoredProcedure, proName, commandParameters);
                int resultCount = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                result = G_command.Parameters["ReturnValue"].Value;
                return result;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                return result;
            }
            finally
            {
                connection.Close();                
            }
        }
        #endregion




        #region 执行一个无返回值的存储过程
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connection">数据库连接字符串</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>
        public int ExecuteNonQueryWithPro(DbConnection connection, string proName, params DbParameter[] commandParameters)
        {
            try
            {
                errorText = string.Empty;
                PrepareCommand(this.G_command, this.G_connection, null, CommandType.StoredProcedure, proName, commandParameters);
                int resultCount = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return resultCount;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                connection.Close();
            }
        }
        #endregion

        #region 执行一个无返回值的命令或过程
        /// <summary>
        /// 执行一个无返回值的命令或过程 
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如:  
        ///  int result = ExecuteNonQuery(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="conn">一个已经存在链接对象</param>
        /// <param name="commandType">命令执行类型(stored procedure, text, etc.)</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>
        public int ExecuteNonQuery(DbConnection connection, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {
            try
            {
                errorText = string.Empty;
                PrepareCommand(this.G_command, this.G_connection, null, cmdType, cmdText, commandParameters);
                int val = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                //return 0;
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                connection.Close();
                //connection.Dispose();
            }

        }
        #endregion

        #region   执行一个无返回值的命令或过程
        /// <summary>
        /// 执行一个无返回值的命令或过程
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 例如：  
        ///  int result = ExecuteNonQuery(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="trans">一个已经存在的事务对象</param>
        /// <param name="commandType">命令执行类型(stored procedure, text, etc.)</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>该操作影响的行数</returns>
        public int ExecuteNonQuery(DbTransaction trans, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {
            try
            {
                PrepareCommand(this.G_command, trans.Connection, trans, cmdType, cmdText, commandParameters);
                int val = G_command.ExecuteNonQuery();
                G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                trans.Connection.Close();
            }
        }
        #endregion

        #region  执行数据命令返回相应结果集（DbDataReader）
        /// <summary>
        /// 执行数据命令返回相应结果集（DbDataReader）
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 举例:  
        ///  DbDataReader r = ExecuteReader(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connString">数据库链接字符串</param>
        /// <param name="commandType">命令执行类型(stored procedure, text, etc.)</param>
        /// <param name="commandText">存储过程名或sql语句</param>
        /// <param name="commandParameters">使用到的参数列表</param>
        /// <returns>DbDataReader类型结果集</returns>
        public DbDataReader ExecuteReader(string connectionString, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {
            errorText = string.Empty;
            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connectionString;
            G_connection.Open();


            try
            {
                PrepareCommand(this.G_command, G_connection, null, cmdType, cmdText, commandParameters);
                DbDataReader rdr = G_command.ExecuteReader(CommandBehavior.CloseConnection);
                G_command.Parameters.Clear();
                return rdr;
            }
            catch(Exception exp)
            {
                errorText = exp.Message;
                return null;
                //conn.Close();
                //throw;
            }
            finally
            {
                G_connection.Close();

            }
        }
        #endregion

        #region 执行带事务的多条sql语句
        /// <summary>
        /// 执行带事务的多条sql语句
        /// </summary>
        /// <param name="sqls">需要执行的sql语句数组</param>
        /// <returns>是否成功</returns>
        public bool ExecMulSqlWithTran(string connectionString, string[] sqls)
        {
            errorText = string.Empty;

            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connectionString;
            G_connection.Open();

            DbTransaction DbTransaction = G_connection.BeginTransaction();

            this.G_command.CommandType = CommandType.Text;
            G_command.Transaction = DbTransaction;
            G_command.Connection = G_connection;
            try
            {
                for (int i = 0; i < sqls.Length; i++)
                {
                    G_command.CommandText = sqls[i];
                    G_command.ExecuteNonQuery();
                }

                DbTransaction.Commit();
                return true;
            }
            catch(Exception exp)
            {
                errorText = exp.Message;
                DbTransaction.Rollback();
                return false;
            }
            finally
            {
                this.G_connection.Close();
                //connection.Dispose();
            }
        }
        #endregion

        #region 执行带事务的多条sql语句
        /// <summary>
        /// 执行带事务的多条sql语句
        /// </summary>
        /// <param name="sqls">需要执行的sql语句数组</param>
        /// <returns>是否成功</returns>
        public bool ExecMulSqlWithTran(string connectionString, List<string> sqls)
        {
            errorText = string.Empty;

            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connectionString;
            G_connection.Open();

            DbTransaction DbTransaction = G_connection.BeginTransaction();

            this.G_command.CommandType = CommandType.Text;
            G_command.Transaction = DbTransaction;
            G_command.Connection = G_connection;
            try
            {
                for (int i = 0; i < sqls.Count; i++)
                {
                    G_command.CommandText = sqls[i];
                    G_command.ExecuteNonQuery();
                }

                DbTransaction.Commit();
                return true;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                DbTransaction.Rollback();
                return false;
            }
            finally
            {
                this.G_connection.Close();
                //connection.Dispose();
            }
        }
        #endregion



        #region  执行sql命令返回相应结果集（DataSet,含参数列表）
        /// <summary>
        /// 执行sql命令返回相应结果集（DataSet,含参数列表）
        /// </summary>
        /// <param name="connectionString">连接字符串</param>
        /// <param name="selectText">sql语句</param>
        /// <param name="parms">参数列表</param>
        /// <returns>执行sql语句后获取的DataSet</returns>     
        public DataSet GetDataSetWithSql(string connectionString, string selectText, DbParameter[] parms)
        {
            errorText = string.Empty;
            DataSet ds = new DataSet();

            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connectionString;
            G_connection.Open();

            DbTransaction DbTransaction = G_connection.BeginTransaction();

            this.G_command.CommandText = selectText;
            G_command.Connection = G_connection;
            G_command.Transaction = DbTransaction;
            //new DbCommand(selectText, connection, DbTransaction);
            PrepareCommand(G_command, CommandType.Text, parms);
            this.G_dataAdapter.SelectCommand = G_command;
            try
            {
                //获取主键信息
                this.G_dataAdapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;
                G_dataAdapter.Fill(ds);
                DbTransaction.Commit();
                return ds;
            }
            catch (Exception exp)
            {
                try
                {
                    ds.Tables.Clear();
                    G_dataAdapter.Fill(ds);
                    DbTransaction.Commit();
                    return ds;

                }
                catch (Exception exp1)
                {
                    errorText = exp1.Message;
                    DbTransaction.Rollback();
                    //MessageBo exp1;
                    return null;
                }
            }
            finally
            {
                this.G_connection.Close();
                //connection.Dispose();

            }

        }
        #endregion

        #region  通过存储过程名成来获取数据集，并返回该数据集（DataSet）
        /// <summary>
        /// 通过存储过程名成来获取数据集， 并向目标表中填充数据  
        /// 需要参数列表的形式
        /// 启用事务的方式
        /// </summary>
        /// <param name="procName">存储过程</param>
        /// <param name="commandParameters">参数数组</param>
        /// <returns>返回dataset</returns>
        public DataSet GetDataSetWithProc(string connectionString, string procName, params DbParameter[] commandParameters)
        {
            errorText = string.Empty;

            DataSet dataSet = new DataSet();

            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connectionString;
            G_connection.Open();


            DbTransaction DbTransaction = G_connection.BeginTransaction();

            G_command.Transaction = DbTransaction;
            G_command.CommandType = CommandType.StoredProcedure;
            G_command.CommandText = procName;
            G_command.Connection = G_connection;
            PrepareCommand(this.G_command, G_connection, null, CommandType.StoredProcedure, procName, commandParameters);

            this.G_dataAdapter.SelectCommand = G_command;
            try
            {
                //获取主键信息
                G_dataAdapter.MissingSchemaAction = MissingSchemaAction.AddWithKey;

                G_dataAdapter.Fill(dataSet);
                DbTransaction.Commit();
                return dataSet;
            }
            catch (Exception exp)
            {
                try
                {
                    dataSet.Tables.Clear();
                    G_dataAdapter.Fill(dataSet);
                    DbTransaction.Commit();
                    return dataSet;

                }
                catch (Exception exp1)
                {
                    errorText = exp1.Message;
                    DbTransaction.Rollback();
                    return null;
                }
            }
            finally
            {
                this.G_connection.Close();
                //conn.Dispose();
            }

        }
        #endregion

        #region 执行数据库操作命令，返回第一行第一列的数据
        /// <summary>
        /// 执行数据库操作命令，返回第一行第一列的数据
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 举例.:  
        ///  Object obj = ExecuteScalar(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connString">数据库连接字符串</param>
        /// <param name="commandType">命令执行模式</param>
        /// <param name="commandText">存储过程或sql语句</param>
        /// <param name="commandParameters">参数列表</param>
        /// <returns>返回的结果（使用时可能需要用convert来进行转换）</returns>
        public object ExecuteScalar(string connectionString, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {

            errorText = string.Empty;
            try
            {
                if (G_connection != null)
                {
                    G_connection.Close();
                }
                this.G_connection.ConnectionString = connectionString;
                G_connection.Open();


                PrepareCommand(this.G_command, G_connection, null, cmdType, cmdText, commandParameters);
                object val = G_command.ExecuteScalar();
                G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                G_connection.Close();
                //connection.Dispose();
                return null;
            }
            finally
            {
                G_connection.Close();
            }
        }
        #endregion

        #region 执行数据库操作命令，返回第一行第一列的数据
        /// <summary>
        /// 执行数据库操作命令，返回第一行第一列的数据
        /// 需要参数列表
        /// </summary>
        /// <remarks>
        /// 举例.:  
        ///  Object obj = ExecuteScalar(connString, CommandType.StoredProcedure, "PublishOrders", new DbParameter("@prodid", 24));
        /// </remarks>
        /// <param name="connection">数据库连接对象</param>
        /// <param name="cmdType">命令执行模式</param>
        /// <param name="cmdText">存储过程或sql语句</param>
        /// <param name="commandParameters">参数列表</param>
        /// <returns>返回的结果（使用时可能需要用convert来进行转换）</returns>
        public object ExecuteScalar(DbConnection connection, CommandType cmdType, string cmdText, params DbParameter[] commandParameters)
        {
            try
            {
                errorText = string.Empty;
                PrepareCommand(G_command, G_connection, null, cmdType, cmdText, commandParameters);
                object val = this.G_command.ExecuteScalar();
                this.G_command.Parameters.Clear();
                return val;
            }
            catch (Exception exp)
            {
                errorText = exp.Message;
                return null;
                //throw;
            }
            finally
            {
                connection.Close();
                // connection.Dispose();
            }


        }
        #endregion

        #region 自动更新并填充数据集(指定DataSet方式)
        /// <summary>
        /// 自动更新并填充数据集(指定DataSet和TableName方式)
        /// </summary>
        /// <param name="connString">数据库连接字符串</param>
        /// <param name="selectText">查询语句</param>
        /// <param name="dataSet">发生改变的数据集（通常写法为ds.GetChanges()）</param>
        /// <param name="cmdParms">参数列表</param>
        /// <returns>影响的行数</returns>
        /// <example> 具体例子如下.
        /// <code>
        ///private void button2_Click(object sender, EventArgs e)
        ///{
        ///    string connStr = "server=127.0.0.1; user id=sa; pwd=;database=pubs";
        ///    PHFLib.DbHelper dbHelper = new DbHelper(DbHelper.DataProviderType.SqlServer, connStr);
        ///    dbHelper.AutoUpdate(dbHelper.ConnString, "SELECT * FROM titles", ds.GetChanges(), null);
        ///}
        ///注：ds为通过dataGridview或其他方式改变的记录集
        /// </code>
        /// </example>

        public int AutoUpdate(
            string connString,
            string selectText,
            DataSet dataSet,
            params DbParameter[] cmdParms
            )
        {
            errorText = string.Empty;
            int resultCount = 0;
            if (dataSet == null)
            {
                return 0;
            }

            DataSet ds = new DataSet();

            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connString;
            G_connection.Open();

            DbTransaction DbTransaction = G_connection.BeginTransaction();

            this.G_command.CommandText = selectText;
            G_command.Connection = G_connection;
            G_command.Transaction = DbTransaction;
            PrepareCommand(G_command, CommandType.Text, cmdParms);

            G_dataAdapter.SelectCommand = G_command;

            G_commandBuilder.DataAdapter = this.G_dataAdapter;
            try
            {
                G_dataAdapter.Fill(ds);
                ds.Merge(dataSet);
                resultCount = G_dataAdapter.Update(ds);
                DbTransaction.Commit();
                return resultCount;
            }
            catch (Exception exp)
            {
                DbTransaction.Rollback();
                this.G_connection.Close();
                //DbConnection.Dispose();
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                G_connection.Close();
            }

        }

        #endregion

        #region 自动更新并填充数据集（指定目标表方式）
        /// <summary>
        /// 自动更新并填充数据集（指定目标表方式）
        /// </summary>
        /// <param name="connString">数据库连接字符串</param>
        /// <param name="selectText">查询语句</param>        
        /// <param name="dataTable">发生数据改变的表（通常写法为dataTable.GetChanges()）</param>
        /// <param name="cmdParms">参数列表</param>
        /// <returns>影响的行数</returns>
        /// <example> 具体例子如下.
        /// <code>
        ///private void button2_Click(object sender, EventArgs e)
        ///{
        ///    string connStr = "server=127.0.0.1; user id=sa; pwd=;database=pubs";
        ///    PHFLib.DbHelper dbHelper = new DbHelper(DbHelper.DataProviderType.SqlServer, connStr);
        ///    dbHelper.AutoUpdate(dbHelper.ConnString, "SELECT * FROM titles", dt.GetChanges(), null);
        ///}
        ///注：dt为通过dataGridview或其他方式改变的datatable
        /// </code>
        /// </example>
        public int AutoUpdate(
            string connString,
            string selectText,
            DataTable dataTable,
            params DbParameter[] cmdParms
            )
        {
            errorText = string.Empty;
            int resultCount = 0;
            if (dataTable == null)
            {
                return 0;
            }
            DataTable dt = new DataTable();
            if (G_connection != null)
            {
                G_connection.Close();
            }
            this.G_connection.ConnectionString = connString;
            G_connection.Open();



            DbTransaction DbTransaction = G_connection.BeginTransaction();

            this.G_command.CommandText = selectText;
            G_command.Connection = G_connection;
            G_command.Transaction = DbTransaction;
            PrepareCommand(G_command, CommandType.Text, cmdParms);

            G_dataAdapter.SelectCommand = G_command;
            //G_command.Transaction =

            this.G_commandBuilder.DataAdapter = G_dataAdapter;
            try
            {
                G_dataAdapter.Fill(dt);
                dt.Merge(dataTable);
                resultCount = G_dataAdapter.Update(dt);
                DbTransaction.Commit();
                return resultCount;
            }
            catch (Exception exp)
            {
                DbTransaction.Rollback();
                this.G_connection.Close();
                errorText = exp.Message;
                return 0;
            }
            finally
            {
                G_connection.Close();
            }

        }

        #endregion

        #region 存储接受到的参数到缓存（暂时不用）
        /// <summary>
        /// 存储接收的参数到缓存
        /// </summary>
        /// <param name="cacheKey">缓存关键字</param>
        /// <param name="cmdParms">参数列表</param>
        public void CacheParameters(string cacheKey, params DbParameter[] commandParameters)
        {
            parmCache[cacheKey] = commandParameters;
        }
        #endregion

        #region 获取缓存参数（暂时不用）
        /// <summary>
        /// 获取缓存参数
        /// </summary>
        /// <param name="cacheKey">需要恢复的关键字</param>
        /// <returns>缓存参数列表</returns>
        public DbParameter[] GetCachedParameters(string cacheKey)
        {
            DbParameter[] cachedParms = (DbParameter[])parmCache[cacheKey];

            if (cachedParms == null)
                return null;

            DbParameter[] clonedParms = new DbParameter[cachedParms.Length];

            for (int i = 0, j = cachedParms.Length; i < j; i++)
                clonedParms[i] = (DbParameter)((ICloneable)cachedParms[i]).Clone();

            return clonedParms;
        }
        #endregion       


        //---------------------------2018-04-17---------------------------------------------
        /// <summary>
        /// datatable转实体类
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dt"></param>
        /// <returns></returns>
        public IList<T> DataTableToModel<T>(DataTable dt) where T : new() //之前没有加where T : new()这个约束，导致后面无法利用T来新建一个实例。
        {
            IList<T> modelList = new List<T>();
            PropertyInfo[] properties = typeof(T).GetProperties();        //利用反射来获取抽象类的各个属性集合
            foreach (DataRow dr in dt.Rows)
            {
                T model = new T();
                foreach (var p in properties)
                {
                    p.SetValue(model, dr[p.Name],null);      //SetValue用于将值设置到属性中去，对应的还有GetValue，第一个参数是要设置属性的对象，第二个参数是要设置的值
                }
                modelList.Add(model);
            }
            return modelList;
        }
        //------------------------------------------------------------------------------------
        #endregion

    }
    #endregion
}
