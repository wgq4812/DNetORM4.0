﻿
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNet.DataAccess
{
    public class OracleDataBase : IDatabase
    {
        /// <summary>
        /// 数据库连接字符串
        /// </summary>
        private string connectionString = String.Empty;

        public string ConnectionString
        {
            get
            {
                return connectionString;
            }
        }

        public OracleTransaction CurrentTransaction { get; set; }

        private OracleConnection dbConnection = null;

        public OracleConnection CurrentDbConnection
        {
            get
            {
                return dbConnection;
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connectionString">连接字符串</param>
        public OracleDataBase(string connectionString)
        {
            this.connectionString = connectionString;
            if (dbConnection == null)
            {
                dbConnection = new OracleConnection(this.connectionString);
            }
        }

        /// <summary>
        /// 构造函数
        /// </summary>
        public OracleDataBase()
        {
            ConnectionStringSettings connSetting = ConfigurationManager.ConnectionStrings["MasterDB"];
            if (connSetting == null)
            {
                throw new Exception("未配置主数据库连接！");
            }
            if (dbConnection == null)
            {
                dbConnection = new OracleConnection(connSetting.ConnectionString);
            }
        }

        /// <summary>
        /// 数据库类型
        /// </summary>
        public DataBaseType DBType
        {
            get { return DataBaseType.Oracle; }
        }

        private void PrepareCommand(OracleCommand cmd, OracleConnection conn, OracleTransaction trans, CommandType cmdType, params DbParameter[] cmdParms)
        {
            if (conn.State != ConnectionState.Open)
                conn.Open();
            cmd.Connection = conn;
            if (trans != null)
                cmd.Transaction = trans;
            cmd.CommandType = cmdType;
            if (cmdParms != null)
            {
                cmd.Parameters.AddRange(cmdParms);
            }
        }

        #region<<生成实体工具>>

        /// <summary>
        /// 生成实体类
        /// </summary>
        /// <param name="nameSpace"></param>
        /// <param name="baseClass">父类</param>
        /// <returns></returns>
        public int GenerateEntities(string nameSpace, Type baseClass = null)
        {
            using (var conn = new OracleConnection(connectionString))
            {
                OracleCommand command = new OracleCommand();
                command.CommandTimeout = 120;
                command.Connection = conn;
                conn.Open();
                DataTable dt = conn.GetSchema("Tables");
                foreach (DataRow row in dt.Rows)
                {
                    var tableName = row["TABLE_NAME"];
                    string generatePath = System.AppDomain.CurrentDomain.BaseDirectory + "bin\\generate\\" + tableName + ".cs";
                    if (!Directory.Exists(System.AppDomain.CurrentDomain.BaseDirectory + "bin\\generate\\"))
                    {
                        Directory.CreateDirectory(System.AppDomain.CurrentDomain.BaseDirectory + "bin\\generate\\");
                    }
                    if (File.Exists(generatePath))
                    {
                        File.Delete(generatePath);
                    }
                    FileStream fs1 = new FileStream(generatePath, FileMode.Create, FileAccess.Write);//创建写入文件 
                    StreamWriter sw = new StreamWriter(fs1);
                    if (baseClass != null)
                    {
                        sw.Write("using " + baseClass.Namespace + ";\r\n");//开始写入值
                    }
                    sw.Write("using System;\r\nusing System.Collections.Generic;\r\nnamespace " + nameSpace + "\r\n{\r\n    public class " + tableName);//开始写入值
                    List<string> baseClassProperties = new List<string>();
                    if (baseClass != null)
                    {
                        sw.Write(" : " + baseClass.Name);
                        baseClassProperties = baseClass.GetProperties().Select(m => m.Name).ToList();
                    }
                    sw.Write("\r\n    {\r\n");
                    command.CommandText = string.Format("SELECT * FROM \"{0}\"", tableName);
                    OracleDataReader dr = command.ExecuteReader(CommandBehavior.KeyInfo);
                    DataTable Schema = dr.GetSchemaTable();
                    foreach (DataRow field in Schema.Rows)
                    {
                        var coms = field.Table.Columns;
                        string columnName = field[coms.IndexOf("ColumnName")].ToString();
                        bool isKey = Convert.ToBoolean(field[coms.IndexOf("IsKey")]);
                        bool isAutoIncrement = Convert.ToBoolean(field[coms.IndexOf("IsAutoIncrement")]);
                        if (!baseClassProperties.Contains(columnName))
                        {
                            var columnType = field[coms.IndexOf("DataType")];
                            string typeBrief = string.Empty;
                            switch (columnType.ToString())
                            {
                                case "System.Int16":
                                case "System.UInt16":
                                    typeBrief = "short?";
                                    break;
                                case "System.Int32":
                                case "System.UInt32":
                                    typeBrief = "int?";
                                    break;
                                case "System.UInt64":
                                case "System.Int64":
                                    typeBrief = "long?";
                                    break;
                                case "System.Boolean":
                                    typeBrief = "bool?";
                                    break;
                                case "System.String":
                                    typeBrief = "string";
                                    break;
                                case "System.DateTime":
                                    typeBrief = "DateTime?";
                                    break;
                                case "System.SByte":
                                case "System.Byte":
                                    typeBrief = "byte?";
                                    break;
                                case "System.Decimal":
                                    typeBrief = "decimal?";
                                    break;
                                case "System.Single":
                                    typeBrief = "float?";
                                    break;
                                case "System.Double":
                                    typeBrief = "double?";
                                    break;
                                case "System.Byte[]":
                                    typeBrief = "byte[]";
                                    break;
                                default:
                                    break;
                            }
                            if (isKey)
                            {
                                if (isAutoIncrement)
                                    sw.WriteLine("        [Key(IsAutoGenerated = true)]");
                                else
                                    sw.WriteLine("        [Key]");
                            }
                            sw.WriteLine("        public " + typeBrief + " " + columnName + " { get; set; }");
                        }
                    }
                    sw.WriteLine("    }");
                    sw.WriteLine("}\r\n");
                    sw.Close();
                    fs1.Close();
                    dr.Close();
                }
                conn.Close();
                return 1;
            }
        }

        #endregion

        #region  << 执行SQL语句 >>

        /// <summary>
        /// 执行SQL语句，返回影响的记录数
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <returns>影响的记录数</returns>
        public int ExecuteSql(string strSql)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            return effectLines;
        }

        /// <summary>
        /// 执行SQL语句，返回影响的记录数
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <param name="cmdParms">查询参数</param>
        /// <returns>影响的记录数</returns>
        public int ExecuteSql(string strSql, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            return effectLines;
        }

        public int ExecuteSqlIdentity(string strSql)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            object effectLines = command.ExecuteScalar();
            command.Dispose();
            if (effectLines != null)
            {
                return Convert.ToInt32(effectLines);
            }
            return 0;
        }

        public int ExecuteSqlIdentity(string strSql, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            object effectLines = command.ExecuteScalar();
            command.Parameters.Clear();
            command.Dispose();
            if (effectLines != null)
            {
                return Convert.ToInt32(effectLines);
            }
            return 0;
        }


        /// <summary>
        /// 执行SQL语句，返回影响的记录数(对于长时间查询的语句，设置等待时间避免查询超时)
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <param name="times">SQL执行过期时间</param>
        /// <returns></returns>
        public int ExecuteSqlByTime(string strSql, int times)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            return effectLines;
        }

        /// <summary>
        /// 执行SQL语句，返回影响的记录数(对于长时间查询的语句，设置等待时间避免查询超时)
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <param name="times">SQL执行过期时间</param>
        /// <param name="cmdParms">查询参数</param>
        /// <returns></returns>
        public int ExecuteSqlByTime(string strSql, int times, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            command.CommandTimeout = times;
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.Dispose();
            return effectLines;
        }

        /// <summary>
        /// 执行一条计算查询结果语句，返回查询结果
        /// </summary>
        /// <param name="strSql">计算查询结果语句</param>
        /// <returns>查询结果</returns>
        public T GetSingle<T>(string strSql)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            object obj = null;
            obj = command.ExecuteScalar();
            command.Dispose();
            if (obj == null
                || obj == System.DBNull.Value)
                return default(T);

            if (typeof(T) == typeof(int))
                obj = Convert.ToInt32(obj);
            return (T)obj;

        }

        /// <summary>
        /// 执行一条计算查询结果语句，返回查询结果
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <param name="cmdParms">查询参数</param>
        /// <returns>查询结果</returns>
        public T GetSingle<T>(string strSql, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            object obj = null;
            obj = command.ExecuteScalar();
            command.Parameters.Clear();
            command.Dispose();
            if (obj == null
                || obj == System.DBNull.Value)
                return default(T);
            if (typeof(T) == typeof(int))
                obj = Convert.ToInt32(obj);
            return (T)obj;
        }

        /// <summary>
        /// 执行查询语句，返回SqlDataReader ( 注意：使用后一定要对SqlDataReader进行Close )
        /// </summary>
        /// <param name="strSql">查询语句</param>
        /// <returns>SqlDataReader</returns>
        public IDataReader ExecuteReader(string strSql)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            OracleDataReader dr = null;
            try
            {
                dr = command.ExecuteReader(CommandBehavior.Default);
            }
            catch (Exception ex)
            {
                CurrentDbConnection.Close();
                CurrentDbConnection.Dispose();
                throw ex;
            }
            return dr;
        }

        /// <summary>
        /// 执行查询语句，返回SqlDataReader ( 注意：使用后一定要对SqlDataReader进行Close )
        /// </summary>
        /// <param name="strSql">SQL语句</param>
        /// <param name="cmdParms">查询参数</param>
        /// <returns>SqlDataReader</returns>
        public IDataReader ExecuteReader(string strSql, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            OracleDataReader dr = null;
            try
            {
                dr = command.ExecuteReader(CommandBehavior.Default);
            }
            catch (Exception ex)
            {
                CurrentDbConnection.Close();
                CurrentDbConnection.Dispose();
                throw ex;
            }
            finally
            {
                command.Parameters.Clear();
            }
            return dr;

        }

        /// <summary>
        /// 执行查询语句，返回DataSet
        /// </summary>
        /// <param name="strSql">查询语句</param>
        /// <returns>DataSet</returns>
        public DataSet Query(string strSql)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            OracleDataAdapter adpter = new OracleDataAdapter(command);
            DataSet ds = new DataSet();
            adpter.Fill(ds);
            return ds;
        }

        /// <summary>
        /// 执行查询语句，返回DataSet
        /// </summary>
        /// <param name="strSql">查询语句</param>
        /// <param name="cmdParms">查询参数</param>
        /// <returns>DataSet</returns>
        public DataSet Query(string strSql, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            OracleDataAdapter adpter = new OracleDataAdapter(command);
            DataSet ds = new DataSet();
            adpter.Fill(ds);
            command.Parameters.Clear();
            command.Dispose();
            return ds;
        }

        /// <summary>
        /// (对于长时间查询的语句，设置等待时间避免查询超时)
        /// </summary>
        /// <param name="strSql">查询语句</param>
        /// <param name="times"></param>
        public DataSet Query(string strSql, int times)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, null);
            command.CommandTimeout = times;
            OracleDataAdapter adpter = new OracleDataAdapter(command);
            DataSet ds = new DataSet();
            adpter.Fill(ds);
            command.Dispose();
            return ds;

        }

        /// <summary>
        /// (对于长时间查询的语句，设置等待时间避免查询超时)
        /// </summary>
        /// <param name="strSql">查询语句</param>
        /// <param name="cmdParms">查询参数</param>
        /// <param name="times"></param>
        public DataSet Query(string strSql, int times, params DbParameter[] cmdParms)
        {
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            command.CommandTimeout = times;
            OracleDataAdapter adpter = new OracleDataAdapter(command);
            DataSet ds = new DataSet();
            adpter.Fill(ds);
            command.Parameters.Clear();
            command.Dispose();
            return ds;
        }

        public DataSet Query(string strSql, string orderText, params DbParameter[] cmdParms)
        {
            string orderBy = string.Empty;
            if (!String.IsNullOrEmpty(orderText))
            {
                orderBy = string.Format(" ORDER BY {0} ", orderText);
                strSql += orderBy;
            }
            OracleCommand command = new OracleCommand(strSql);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.Text, cmdParms);
            OracleDataAdapter adpter = new OracleDataAdapter(command);
            DataSet ds = new DataSet();
            adpter.Fill(ds);
            command.Parameters.Clear();
            command.Dispose();
            return ds;
        }

        #endregion

        #region << 存储过程操作 >>

        /// <summary>
        /// 执行存储过程，返回影响的行数       
        /// </summary>       
        public int RunProcedure(string storedProcName)
        {
            OracleCommand command = new OracleCommand(storedProcName);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.StoredProcedure, null);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            command.Dispose();
            return effectLines;
        }

        /// <summary>
        /// 执行存储过程，返回影响的行数       
        /// </summary>       
        public int RunProcedure(string storedProcName, params DbParameter[] parameters)
        {
            OracleCommand command = new OracleCommand(storedProcName);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.StoredProcedure, parameters);
            int effectLines = 0;
            effectLines = command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.Dispose();
            return effectLines;

        }

        /// <summary>
        /// 执行存储过程，返回输出参数的值和影响的行数       
        /// </summary>
        /// <param name="storedProcName">存储过程名</param>
        /// <param name="parameters">存储过程参数</param>
        /// <param name="OutParameter">输出参数名称</param>
        /// <param name="rowsAffected">影响的行数</param>
        /// <returns></returns>
        public object RunProcedure(string storedProcName, DbParameter OutParameter, params DbParameter[] InParameters)
        {
            OracleCommand command = new OracleCommand(storedProcName);
            PrepareCommand(command, CurrentDbConnection, CurrentTransaction, CommandType.StoredProcedure, null);
            command.Parameters.Add(OutParameter);
            command.Parameters.AddRange(InParameters);
            command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.Dispose();
            return OutParameter.Value;

        }

        /// <summary>
        /// 执行存储过程，返回SqlDataReader ( 注意：使用后一定要对SqlDataReader进行Close )
        /// </summary>
        /// <param name="storedProcName">存储过程名</param>
        /// <param name="parameters">存储过程参数</param>
        /// <returns>SqlDataReader</returns>
        public IDataReader RunProcedureReader(string storedProcName, params DbParameter[] parameters)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region << 分页数据操作 >>

        /// <summary>
        /// 执行分页查询
        /// </summary>
        /// <param name="sqlText">SQL语句</param>
        /// <param name="orderText"></param>
        /// <param name="pageIndex">当前页的页码</param>
        /// <param name="pageSize">每页记录数</param>
        /// <param name="recordCount">输出参数，返回查询的总记录条数</param>
        /// <param name="pageCount">输出参数，返回查询的总页数</param>
        /// <param name="currentPageIndex">输出参数，返回当前页面索引</param>
        /// <param name="commandParameters">查询参数</param>
        /// <returns>返回查询结果</returns>
        public IDataReader ExecutePageReader(string sqlText, string orderText, int pageIndex, int pageSize, out int recordCount, out int pageCount, out int currentPageIndex, params DbParameter[] commandParameters)
        {
            // 得到总记录数
            string sqlTextCount = String.Format("SELECT COUNT(1) FROM ({0}) t", sqlText);
            recordCount = this.GetSingle<int>(sqlTextCount, commandParameters);

            // 计算总页面数
            if (pageSize <= 0)
            {
                pageSize = 20;
            }
            if (recordCount % pageSize > 0)
            {
                pageCount = recordCount / pageSize + 1;
            }
            else
            {
                pageCount = recordCount / pageSize;
            }

            // 得到当前页面索引
            if (pageIndex < 1)
                pageIndex = 1;
            currentPageIndex = pageIndex;
            if (currentPageIndex > pageCount)
            {
                currentPageIndex = pageCount;
            }

            if (!String.IsNullOrEmpty(orderText))
            {
                orderText = " ORDER BY " + orderText + " NULLS LAST ";
            }

            // 得到用于分页的SQL语句
            int startIndex = (currentPageIndex - 1) * pageSize;
            int endIndex = currentPageIndex * pageSize;
            string sqlTextRecord = @"SELECT * FROM (SELECT tt1.*, ROWNUM rn  FROM ({0} {1}) tt1 WHERE ROWNUM <= {3}) tt2 WHERE rn > {2} ";
            sqlTextRecord = String.Format(sqlTextRecord, sqlText, orderText, startIndex, endIndex);

            return ExecuteReader(sqlTextRecord, commandParameters);
        }
        public IDataReader ExecutePageReader(string sqlText, string orderText, int pageIndex, int pageSize, out int recordCount, out int pageCount, out int currentPageIndex)
        {
            // 得到总记录数
            string sqlTextCount = String.Format("SELECT COUNT(1) FROM ({0}) t", sqlText);
            recordCount = this.GetSingle<int>(sqlTextCount);

            // 计算总页面数
            if (pageSize <= 0)
            {
                pageSize = 20;
            }
            if (recordCount % pageSize > 0)
            {
                pageCount = recordCount / pageSize + 1;
            }
            else
            {
                pageCount = recordCount / pageSize;
            }

            // 得到当前页面索引
            if (pageIndex < 1)
                pageIndex = 1;
            currentPageIndex = pageIndex;
            if (currentPageIndex > pageCount)
            {
                currentPageIndex = pageCount;
            }

            if (!String.IsNullOrEmpty(orderText))
            {
                orderText = " ORDER BY " + orderText + " NULLS LAST ";
            }

            // 得到用于分页的SQL语句
            int startIndex = (currentPageIndex - 1) * pageSize;
            int endIndex = currentPageIndex * pageSize;
            string sqlTextRecord = @"SELECT * FROM (SELECT tt1.*, ROWNUM rn  FROM ({0} {1}) tt1 WHERE ROWNUM <= {3}) tt2 WHERE rn > {2} ";
            sqlTextRecord = String.Format(sqlTextRecord, sqlText, orderText, startIndex, endIndex);

            return ExecuteReader(sqlTextRecord);
        }

        /// <summary>
        /// 执行分页查询
        /// </summary>
        /// <param name="sqlText">SQL语句</param>
        /// <param name="orderText"></param>
        /// <param name="pageIndex">当前页的页码</param>
        /// <param name="pageSize">每页记录数</param>
        /// <param name="recordCount">输出参数，返回查询的总记录条数</param>
        /// <param name="pageCount">输出参数，返回查询的总页数</param>
        /// <param name="currentPageIndex">输出参数，返回当前页面索引</param>
        /// <param name="commandParameters">查询参数</param>
        /// <returns>返回查询结果</returns>
        public DataTable ExecutePage(string sqlText, string orderText, int pageIndex, int pageSize, out int recordCount, out int pageCount, out int currentPageIndex, params DbParameter[] commandParameters)
        {
            // 得到总记录数
            string sqlTextCount = String.Format("SELECT COUNT(1) FROM ({0}) t", sqlText);
            recordCount = this.GetSingle<int>(sqlTextCount, commandParameters);

            // 计算总页面数
            if (pageSize <= 0)
            {
                pageSize = 20;
            }
            if (recordCount % pageSize > 0)
            {
                pageCount = recordCount / pageSize + 1;
            }
            else
            {
                pageCount = recordCount / pageSize;
            }

            // 得到当前页面索引
            if (pageIndex < 1)
                pageIndex = 1;
            currentPageIndex = pageIndex;
            if (currentPageIndex > pageCount)
            {
                currentPageIndex = pageCount;
            }

            if (!String.IsNullOrEmpty(orderText))
            {
                orderText = " ORDER BY " + orderText + " NULLS LAST ";
            }

            // 得到用于分页的SQL语句
            int startIndex = (currentPageIndex - 1) * pageSize;
            int endIndex = currentPageIndex * pageSize;
            string sqlTextRecord = @"SELECT * FROM (SELECT tt1.*, ROWNUM rn  FROM ({0} {1} ) tt1 WHERE ROWNUM <= {3}) tt2 WHERE rn > {2} ";
            sqlTextRecord = String.Format(sqlTextRecord, sqlText, orderText, startIndex, endIndex);

            return Query(sqlTextRecord, commandParameters).Tables[0];
        }

        #endregion

        #region << 数据库语句差异处理 >>

        /// <summary>
        /// 数据库参数连接符
        /// </summary>
        public string ParameterPrefix
        {
            get { return ":"; }
        }

        /// <summary>
        /// 得到数据库SQL参数对象
        /// </summary>
        /// <param name="parameterName">参数名</param>
        /// <param name="value">参数值</param>
        /// <returns>返回SQL参数对象</returns>
        public DbParameter GetDbParameter(string parameterName, object value)
        {
            if (value.GetType() == typeof(bool) || value.GetType() == typeof(bool?))
            {
                value = Convert.ToInt32(value);
            }
            return new OracleParameter(ParameterPrefix + parameterName, value);
        }

        /// <summary>
        /// 得到数据库SQL参数对象
        /// </summary>
        /// <param name="parameterName">参数名</param>
        /// <param name="value">参数值</param>
        /// <param name="dbType">数据类型</param>
        /// <returns>返回SQL参数对象</returns>
        public DbParameter GetDbParameter(string parameterName, object value, DbType dbType)
        {
            OracleParameter parameter = new OracleParameter(ParameterPrefix + parameterName, value);
            parameter.DbType = dbType;
            return parameter;
        }

        #endregion

        #region<<事务>>
        public void BeginTransaction()
        {
            if (CurrentTransaction == null)
            {
                if (CurrentDbConnection.State != ConnectionState.Open)
                    CurrentDbConnection.Open();
                CurrentTransaction = CurrentDbConnection.BeginTransaction();
            }
        }

        public void BeginTransaction(IsolationLevel level)
        {
            if (CurrentTransaction == null)
            {
                if (CurrentDbConnection.State != ConnectionState.Open)
                    CurrentDbConnection.Open();
                CurrentTransaction = CurrentDbConnection.BeginTransaction(level);
            }
        }

        public void Rollback()
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Rollback();
                CurrentTransaction = null;
            }
        }

        public void Commit()
        {
            if (CurrentTransaction != null)
            {
                CurrentTransaction.Commit();
                CurrentTransaction = null;
            }
        }

        public void Dispose()
        {
            if (CurrentDbConnection != null)
            {
                if (CurrentDbConnection.State != ConnectionState.Closed)
                {
                    if (CurrentTransaction != null)
                        CurrentTransaction.Commit();
                    CurrentDbConnection.Close();
                }
                CurrentDbConnection.Dispose();
                CurrentTransaction = null;
            }
        }
        #endregion

    }
}
