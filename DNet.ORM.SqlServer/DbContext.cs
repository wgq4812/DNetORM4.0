﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using System.Reflection;
using System.Data.Common;
using System.Diagnostics;
using System.Transactions;
using System.Linq.Expressions;
using DNet.Cache;
using DNet.Transaction;
using System.Dynamic;
using System.Configuration;
using DNet.DataAccess.Dialect;

namespace DNet.DataAccess
{
    /// <summary>
    /// DbContext
    /// </summary>
    public class DbContext : IDisposable
    {
        public DbContext()
        {
            this.BatchSize = 100;
            DataBase = DbFactory.CreateDataBase();
            SqlDialect = SqlDialectFactory.CreateSqlDialect();
        }

        public DbContext(ConnectionStringSettings settings)
        {
            //this.EntityInfo = new EntityInfo();
            this.BatchSize = 100;
            DataBase = DbFactory.CreateDataBase(settings);
            SqlDialect = SqlDialectFactory.CreateSqlDialect();
        }

        private int BatchSize { get; set; }

        /// <summary>
        /// 数据库
        /// </summary>
        public IDatabase DataBase { get; set; }

        private BindingFlags BindFlag = BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance;

        private ISqlDialect SqlDialect { get; set; }

        #region <<基于实体增删改查方法>>

        /// <summary>
        /// 准备插入语句
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="insertSql"></param>
        /// <param name="parms"></param>
        protected int InsertT<T>(T entity)
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder insertSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                insertSql.AppendFormat(" INSERT INTO {0} ( ", entityInfo.TableName);
                StringBuilder insertFields = new StringBuilder();
                StringBuilder insertValues = new StringBuilder();

                foreach (PropertyInfo property in entityInfo.ColumnProperties)
                {

                    object propertyValue = null;
                    if (!property.Name.Equals(entityInfo.AutoGeneratedKey) && (propertyValue = property.GetValue(entity, null)) != null)
                    {
                        insertFields.AppendFormat(" {0},", entityInfo.Columns[property.Name]);
                        insertValues.AppendFormat("{0}{1},", DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                }
                //如果含有自增列
                if (!string.IsNullOrEmpty(entityInfo.AutoGeneratedKey))
                {
                    insertSql.Append(insertFields.ToString().TrimEnd(','));
                    insertSql.Append(" ) VALUES ( ");
                    insertSql.Append(insertValues.ToString().TrimEnd(','));
                    insertSql.Append(")");
                    insertSql.Append(SqlDialect.SelectIdentity());
                    return DataBase.ExecuteSqlIdentity(insertSql.ToString(), parms.ToArray());
                }
                else
                {
                    insertSql.Append(insertFields.ToString().TrimEnd(','));
                    insertSql.Append(" ) VALUES ( ");
                    insertSql.Append(insertValues.ToString().TrimEnd(','));
                    insertSql.Append(")");
                    return DataBase.ExecuteSql(insertSql.ToString(), parms.ToArray());
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 准备批量插入
        /// </summary>
        /// <param name="entities"></param>
        /// <returns></returns>
        protected int InsertBatchT<T>(List<T> entities)
        {
            int effect = 0;
            if (entities != null && entities.Count > 0)
            {
                //分批次
                int count = entities.Count;
                if (count == 0)
                {
                    return 0;
                }
                int batchSize = BatchSize;
                int batch = (count - 1) / batchSize + 1;
                try
                {
                    EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                    //批次循环
                    for (int b = 0; b < batch; b++)
                    {
                        StringBuilder insertBatchSql = new StringBuilder();
                        List<DbParameter> parms = new List<DbParameter>();
                        //单批次内部循环
                        for (int itemIndex = b * batchSize; itemIndex < batchSize * (b + 1) && itemIndex < count; itemIndex++)
                        {
                            StringBuilder insertSql = new StringBuilder();
                            insertSql.AppendFormat(" INSERT INTO {0} ( ", entityInfo.TableName);
                            StringBuilder insertFields = new StringBuilder();
                            StringBuilder insertValues = new StringBuilder();
                            foreach (PropertyInfo property in entityInfo.ColumnProperties)
                            {
                                object propertyValue = null;
                                IPropertyAccessor propertyAccessor = Caches.PropertyAccessorCache.Get(property);
                                if (!property.Name.Equals(entityInfo.AutoGeneratedKey) && (propertyValue = propertyAccessor.GetValue(entities[itemIndex])) != null)
                                {
                                    insertFields.AppendFormat("{0},", entityInfo.Columns[property.Name]);
                                    insertValues.AppendFormat("{0}{1}{2},", DataBase.ParameterPrefix, property.Name, itemIndex.ToString());
                                    parms.Add(DataBase.GetDbParameter(property.Name + itemIndex.ToString(), propertyValue));
                                }
                            }
                            insertSql.Append(insertFields.ToString().TrimEnd(','));
                            insertSql.Append(" ) VALUES ( ");
                            insertSql.Append(insertValues.ToString().TrimEnd(','));
                            insertSql.Append(")");
                            insertBatchSql.Append(insertSql.ToString() + ";");
                        }
                        effect += DataBase.ExecuteSql(insertBatchSql.ToString(), parms.ToArray());
                    }
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return effect;
        }

        /// <summary>
        /// 准备删除语句
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="deleteSql"></param>
        /// <param name="parms"></param>
        protected int DeleteT<T>(T entity)
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                if (entityInfo.KeyProperties.Count == 0)
                {
                    throw new LambdaLossException("进行Delete操作时，实体没有声明主键特性");
                }
                StringBuilder deleteSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                deleteSql.AppendFormat(" DELETE FROM {0} WHERE ", entityInfo.TableName);
                StringBuilder whereClause = new StringBuilder();
                foreach (PropertyInfo property in entityInfo.KeyProperties)
                {
                    object propertyValue = null;
                    if ((propertyValue = property.GetValue(entity, null)) != null)
                    {
                        whereClause.AppendFormat(" {0}={1}{2} AND", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                }
                deleteSql.Append(whereClause.ToString().TrimEnd("AND".ToArray()));
                return DataBase.ExecuteSql(deleteSql.ToString(), parms.ToArray());
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 根据lambda表达式条件删除操作
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="exp"></param>
        /// <returns></returns>
        protected int DeleteT<T>(Expression<Func<T, bool>> exp) where T : class, new()
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder deleteSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                deleteSql.AppendFormat(" DELETE FROM {0} WHERE ", entityInfo.TableName);
                if (exp != null)
                {
                    SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                    string where = lambdaTranslator.Translate(exp);
                    deleteSql.Append(where);
                    foreach (DbParameter parm in lambdaTranslator.Parameters)
                    {
                        parms.Add(parm);
                    }
                    return DataBase.ExecuteSql(deleteSql.ToString(), parms.ToArray());
                }
                else
                {
                    throw new LambdaLossException("进行Delete操作时，lambda表达式为null");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

        }

        /// <summary>
        /// 批量删除
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        protected int DeleteBatchT<T>(List<T> entities)
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                if (entityInfo.KeyProperties.Count == 0)
                {
                    throw new LambdaLossException("进行Delete操作时，实体没有声明主键特性");
                }
                //分批次
                int count = entities.Count;
                if (count == 0)
                {
                    return 0;
                }
                int batchSize = BatchSize;
                int batch = (count - 1) / batchSize + 1;
                int effect = 0;

                //批次循环
                for (int b = 0; b < batch; b++)
                {
                    StringBuilder deleteBatchSql = new StringBuilder();
                    List<DbParameter> parms = new List<DbParameter>();
                    //单批次内部循环
                    for (int itemIndex = b * batchSize; itemIndex < batchSize * (b + 1) && itemIndex < count; itemIndex++)
                    {
                        StringBuilder deleteSql = new StringBuilder();
                        deleteSql.AppendFormat(" DELETE FROM {0} WHERE ", entityInfo.TableName);
                        StringBuilder whereClause = new StringBuilder();
                        foreach (PropertyInfo property in entityInfo.KeyProperties)
                        {
                            object propertyValue = null;
                            IPropertyAccessor propertyAccessor = Caches.PropertyAccessorCache.Get(property);
                            if ((propertyValue = propertyAccessor.GetValue(entities[itemIndex])) != null)
                            {
                                whereClause.AppendFormat(" {0}={1}{2}{3} AND", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name, itemIndex.ToString());
                                parms.Add(DataBase.GetDbParameter(property.Name + itemIndex.ToString(), propertyValue));
                            }
                        }
                        deleteSql.Append(whereClause.ToString().TrimEnd("AND".ToArray()));
                        deleteBatchSql.Append(deleteSql.ToString() + ";");
                    }
                    effect += DataBase.ExecuteSql(deleteBatchSql.ToString(), parms.ToArray());
                }
                return effect;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 更新sql语句
        /// </summary>
        /// <param name="entity"></param>
        /// <param name="updateSql"></param>
        /// <param name="parms"></param>
        protected int UpdateT<T>(T entity)
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                if (entityInfo.PrimaryKey.Count == 0)
                {
                    throw new KeyLossException();
                }
                StringBuilder updateSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                StringBuilder updateValues = new StringBuilder();
                StringBuilder whereClause = new StringBuilder();
                foreach (PropertyInfo property in entityInfo.NotKeyColumnProperties)
                {
                    object propertyValue = null;
                    if ((propertyValue = property.GetValue(entity, null)) != null)
                    {
                        updateValues.AppendFormat("{0}={1}{2},", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                }
                updateSql.Append(updateValues.ToString().TrimEnd(','));
                updateSql.Append(" WHERE ");
                foreach (PropertyInfo property in entityInfo.KeyProperties)
                {
                    whereClause.AppendFormat(" {0}={1}{2} AND", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                    parms.Add(DataBase.GetDbParameter(property.Name, property.GetValue(entity, null)));
                }
                updateSql.Append(whereClause.ToString().TrimEnd("AND".ToArray()));
                return DataBase.ExecuteSql(updateSql.ToString(), parms.ToArray());
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 根据lambda表达式更新
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        protected int UpdateT<T>(T entity, Expression<Func<T, bool>> exp) where T : class, new()
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder updateSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                StringBuilder updateValues = new StringBuilder();
                StringBuilder whereClause = new StringBuilder();
                foreach (PropertyInfo property in entityInfo.NotKeyColumnProperties)
                {
                    object propertyValue = null;
                    if ((propertyValue = property.GetValue(entity, null)) != null)
                    {
                        updateValues.AppendFormat("{0}={1}{2},", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                }
                updateSql.Append(updateValues.ToString().TrimEnd(','));
                updateSql.Append(" WHERE ");
                if (exp != null)
                {
                    SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                    string where = lambdaTranslator.Translate(exp);
                    updateSql.Append(where);
                    foreach (DbParameter parm in lambdaTranslator.Parameters)
                    {
                        parms.Add(parm);
                    }
                    return DataBase.ExecuteSql(updateSql.ToString(), parms.ToArray());
                }
                else
                {
                    throw new LambdaLossException("进行Update操作时，lambda表达式为null");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        protected int UpdateT<T>(Expression<Func<T, T>> updateExp, Expression<Func<T, bool>> exp) where T : class, new()
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder updateSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                SqlVisitor updateVisitor = new SqlVisitor(this.DataBase.DBType, 0, VisitorType.UpdateSet);
                string updateSet = updateVisitor.Translate(updateExp);
                foreach (DbParameter parm in updateVisitor.Parameters)
                {
                    parms.Add(parm);
                }
                StringBuilder whereClause = new StringBuilder();
                updateSql.Append(updateSet.TrimEnd(','));
                updateSql.Append(" WHERE ");
                if (exp != null)
                {
                    SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType, 1);
                    string where = lambdaTranslator.Translate(exp);
                    updateSql.Append(where);
                    foreach (DbParameter parm in lambdaTranslator.Parameters)
                    {
                        parms.Add(parm);
                    }
                    return DataBase.ExecuteSql(updateSql.ToString(), parms.ToArray());
                }
                else
                {
                    throw new LambdaLossException("进行Update操作时，lambda表达式为null");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        ///  忽略指定字段更新
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="ignoreFields"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        protected int UpdateT<T>(T entity, Expression<Func<T, dynamic>> ignoreFields, Expression<Func<T, bool>> exp) where T : class, new()
        {
            try
            {
                DynamicVisitor visitor = new DynamicVisitor();
                visitor.Translate(ignoreFields);
                List<string> ignores = visitor.DynamicMembers.Select(m => m.Key).ToList();
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder updateSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                StringBuilder updateValues = new StringBuilder();
                StringBuilder whereClause = new StringBuilder();
                foreach (PropertyInfo property in entityInfo.NotKeyColumnProperties)
                {
                    object propertyValue = null;
                    if ((propertyValue = property.GetValue(entity, null)) != null && !ignores.Contains(property.Name))
                    {
                        updateValues.AppendFormat("{0}={1}{2},", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                }
                updateSql.Append(updateValues.ToString().TrimEnd(','));
                updateSql.Append(" WHERE ");
                if (exp != null)
                {
                    SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                    string where = lambdaTranslator.Translate(exp);
                    updateSql.Append(where);
                    foreach (DbParameter parm in lambdaTranslator.Parameters)
                    {
                        parms.Add(parm);
                    }
                    return DataBase.ExecuteSql(updateSql.ToString(), parms.ToArray());
                }
                else
                {
                    throw new LambdaLossException("进行Update操作时，lambda表达式为null");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 根据指定字段更新
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entity"></param>
        /// <param name="updateFields"></param>
        /// <param name="exp"></param>
        /// <returns></returns>
        protected int UpdateT<T>(T entity, List<string> updateFields, Expression<Func<T, bool>> exp) where T : class, new()
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                StringBuilder updateSql = new StringBuilder();
                List<DbParameter> parms = new List<DbParameter>();
                updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                StringBuilder updateValues = new StringBuilder();
                StringBuilder whereClause = new StringBuilder();
                foreach (PropertyInfo property in entityInfo.ColumnProperties.Where(m => updateFields.Contains(m.Name)))
                {
                    object propertyValue = null;
                    if ((propertyValue = property.GetValue(entity, null)) != null)
                    {
                        updateValues.AppendFormat("{0}={1}{2},", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name);
                        parms.Add(DataBase.GetDbParameter(property.Name, propertyValue));
                    }
                    else
                    {
                        updateValues.Append(entityInfo.Columns[property.Name] + "=null,");
                    }
                }
                updateSql.Append(updateValues.ToString().TrimEnd(','));
                updateSql.Append(" WHERE ");
                if (exp != null)
                {
                    SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                    string where = lambdaTranslator.Translate(exp);
                    updateSql.Append(where);
                    foreach (DbParameter parm in lambdaTranslator.Parameters)
                    {
                        parms.Add(parm);
                    }
                    return DataBase.ExecuteSql(updateSql.ToString(), parms.ToArray());
                }
                else
                {
                    throw new LambdaLossException("进行Update操作时，lambda表达式为null");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 准备批量更新
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="entities"></param>
        /// <returns></returns>
        protected int UpdateBatchT<T>(List<T> entities)
        {
            try
            {
                EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
                if (entityInfo.PrimaryKey.Count == 0)
                {
                    throw new KeyLossException();
                }
                //分批次
                int count = entities.Count;
                if (count == 0)
                {
                    return 0;
                }
                int batchSize = BatchSize;
                int batch = (count - 1) / batchSize + 1;
                int effect = 0;

                //批次循环
                for (int b = 0; b < batch; b++)
                {
                    StringBuilder updateBatchSql = new StringBuilder();
                    List<DbParameter> parms = new List<DbParameter>();
                    //单批次内部循环
                    for (int itemIndex = b * batchSize; itemIndex < batchSize * (b + 1) && itemIndex < count; itemIndex++)
                    {
                        StringBuilder updateSql = new StringBuilder();
                        updateSql.AppendFormat(" UPDATE {0} SET ", entityInfo.TableName);
                        StringBuilder updateValues = new StringBuilder();
                        StringBuilder whereClause = new StringBuilder();
                        foreach (PropertyInfo property in entityInfo.NotKeyColumnProperties)
                        {
                            object propertyValue = null;
                            IPropertyAccessor propertyAccessor = Caches.PropertyAccessorCache.Get(property);
                            if ((propertyValue = propertyAccessor.GetValue(entities[itemIndex])) != null)
                            {
                                updateValues.AppendFormat("{0}={1}{2}{3},", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name, itemIndex.ToString());
                                parms.Add(DataBase.GetDbParameter(property.Name + itemIndex.ToString(), propertyValue));
                            }
                        }
                        updateSql.Append(updateValues.ToString().TrimEnd(','));
                        updateSql.Append(" WHERE ");
                        foreach (PropertyInfo property in entityInfo.KeyProperties)
                        {
                            whereClause.AppendFormat(" {0}={1}{2}{3} AND", entityInfo.Columns[property.Name], DataBase.ParameterPrefix, property.Name, itemIndex.ToString());
                            parms.Add(DataBase.GetDbParameter(property.Name + itemIndex.ToString(), property.GetValue(entities[itemIndex], null)));
                        }
                        updateSql.Append(whereClause.ToString().TrimEnd("AND".ToArray()));
                        updateBatchSql.Append(updateSql.ToString() + ";");
                    }
                    effect += DataBase.ExecuteSql(updateBatchSql.ToString(), parms.ToArray());
                }
                return effect;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        protected void GetSQLByLambda<TIn, TResult>(StringBuilder selectSql, List<DbParameter> parms, Expression<Func<TIn, bool>> exp, Expression<Func<TIn, TResult>> select, SelectType selectType) where TIn : class, new()
        {
            EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(TIn));
            selectSql.Append("SELECT ");
            StringBuilder fieldBuilder = new StringBuilder();
            SqlVisitor selectTranslator = new SqlVisitor(this.DataBase.DBType, 0);
            string fields = selectTranslator.Translate(select);
            foreach (DbParameter parm in selectTranslator.Parameters)
            {
                parms.Add(parm);
            }
            switch (selectType)
            {
                case SelectType.Distinct:
                    selectSql.Append("DISTINCT ");
                    selectSql.Append(fields.TrimEnd(','));
                    break;
                case SelectType.Max:
                    fieldBuilder.AppendFormat("MAX({0}) ", fields.TrimEnd(','));
                    selectSql.Append(fieldBuilder.ToString().TrimEnd(','));
                    break;
                case SelectType.Min:
                    fieldBuilder.AppendFormat("MIN({0}) ", fields.TrimEnd(','));
                    selectSql.Append(fieldBuilder.ToString().TrimEnd(','));
                    break;
                case SelectType.Count:
                    selectSql.Append("COUNT(1) CT ");
                    break;
            }
            selectSql.Append(" FROM ");
            selectSql.Append(entityInfo.TableName);
            if (exp != null)
            {
                SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType, 1);
                string where = lambdaTranslator.Translate(exp);
                selectSql.Append(" WHERE ");
                selectSql.Append(where);
                foreach (DbParameter parm in lambdaTranslator.Parameters)
                {
                    parms.Add(parm);
                }
            }
        }

        /// <summary>
        /// 准备查询语句
        /// </summary>
        /// <param name="selectSql"></param>
        /// <param name="parms"></param>
        /// <param name="filterItems"></param>
        protected void GetSQLByLambda<TIn, TResult>(StringBuilder selectSql, List<DbParameter> parms, Expression<Func<TIn, bool>> exp, Expression<Func<TIn, TResult>> select) where TIn : class, new()
        {
            EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(TIn));
            selectSql.Append("SELECT ");
            StringBuilder fieldBuilder = new StringBuilder();
            SqlVisitor selectTranslator = new SqlVisitor(this.DataBase.DBType, 0);
            string fields = selectTranslator.Translate(select);
            foreach (DbParameter parm in selectTranslator.Parameters)
            {
                parms.Add(parm);
            }
            selectSql.Append(fields.TrimEnd(','));
            selectSql.Append(" FROM ");
            selectSql.Append(entityInfo.TableName);
            if (exp != null)
            {
                SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType, 1);
                string where = lambdaTranslator.Translate(exp);
                selectSql.Append(" WHERE ");
                selectSql.Append(where);
                foreach (DbParameter parm in lambdaTranslator.Parameters)
                {
                    parms.Add(parm);
                }
            }
        }

        /// <summary>
        /// 排序
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="selectSql"></param>
        /// <param name="parms"></param>
        /// <param name="exp"></param>
        /// <param name="orderby"></param>
        protected void GetSQLByLambda<T>(StringBuilder selectSql, List<DbParameter> parms, Expression<Func<T, bool>> exp, Expression<Func<IEnumerable<T>, IOrderedEnumerable<T>>> orderby) where T : class, new()
        {
            EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
            selectSql.Append("SELECT ");
            selectSql.Append(entityInfo.SelectFields);
            selectSql.Append(" FROM ");
            selectSql.Append(entityInfo.TableName);
            if (exp != null)
            {
                SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                string where = lambdaTranslator.Translate(exp);
                selectSql.Append(" WHERE ");
                selectSql.Append(where);
                foreach (DbParameter parm in lambdaTranslator.Parameters)
                {
                    parms.Add(parm);
                }
            }
            if (orderby != null)
            {
                OrderByVisitor<T> orderByVisitor = new OrderByVisitor<T>();
                string orderBy = orderByVisitor.Translate(orderby);
                selectSql.Append(" ORDER BY ");
                selectSql.Append(orderBy);
            }
        }

        protected void GetSQLByLambda<T>(StringBuilder selectSql, List<DbParameter> parms, Expression<Func<T, bool>> exp) where T : class, new()
        {
            EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
            selectSql.Append("SELECT ");
            selectSql.Append(entityInfo.SelectFields);
            selectSql.Append(" FROM ");
            selectSql.Append(entityInfo.TableName);
            if (exp != null)
            {
                SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                string where = lambdaTranslator.Translate(exp);
                selectSql.Append(" WHERE ");
                selectSql.Append(where);
                foreach (DbParameter parm in lambdaTranslator.Parameters)
                {
                    parms.Add(parm);
                }
            }
        }

        /// <summary>
        /// 翻译ExistsSQL
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="selectSql"></param>
        /// <param name="parms"></param>
        /// <param name="exp"></param>
        protected void GetExistsSQLByLambda<T>(StringBuilder selectSql, List<DbParameter> parms, Expression<Func<T, bool>> exp) where T : class, new()
        {
            EntityInfo entityInfo = Caches.EntityInfoCache.Get(typeof(T));
            selectSql.Append("SELECT COUNT(1) CT FROM ");
            selectSql.Append(entityInfo.TableName);
            if (exp != null)
            {
                SqlVisitor lambdaTranslator = new SqlVisitor(this.DataBase.DBType);
                string where = lambdaTranslator.Translate(exp);
                selectSql.Append(" WHERE ");
                selectSql.Append(where);
                foreach (DbParameter parm in lambdaTranslator.Parameters)
                {
                    parms.Add(parm);
                }
            }
        }

        #endregion

        #region<<反射辅助方法>>

        protected void SetEntityMembers<T>(IDataReader reader, T entity)
        {
            int count = reader.FieldCount;

            for (int i = 0; i < count; i++)
            {
                PropertyInfo property;
                if (reader[i] != DBNull.Value)
                {
                    //如果列名=实体字段名
                    if ((property = typeof(T).GetProperty(reader.GetName(i), BindFlag)) != null)
                    {
                        IPropertyAccessor propertyAccessor = Caches.PropertyAccessorCache.Get(property);
                        propertyAccessor.SetValue(entity, reader[i]);
                    }
                    else
                    {
                        //如果列名!=实体字段名
                        string propertyname = Caches.EntityInfoCache.Get(typeof(T)).Columns.FirstOrDefault(m => m.Value == reader.GetName(i)).Key;
                        if (!string.IsNullOrEmpty(propertyname) && (property = typeof(T).GetProperty(propertyname, BindFlag)) != null)
                        {
                            IPropertyAccessor propertyAccessor = Caches.PropertyAccessorCache.Get(property);
                            propertyAccessor.SetValue(entity, reader[i]);
                        }
                    }
                }
            }
        }

        protected void SetDictionaryValues(IDataReader reader, Dictionary<string, object> dic)
        {
            int count = reader.FieldCount;
            for (int i = 0; i < count; i++)
            {
                if (reader[i] != DBNull.Value)
                {
                    dic.Add(reader.GetName(i), reader[i]);
                }
                else
                {
                    dic.Add(reader.GetName(i), null);
                }
            }
        }

        protected TObject GetDynamicObject<TObject>(IDataReader reader)
        {
            //为什么不能根据reader列数判断 因为实体存在一个属性的实体
            if (!typeof(TObject).IsClass || typeof(TObject) == typeof(string))
            {
                if (reader[0] != DBNull.Value)
                {
                    if (typeof(TObject) == reader[0].GetType())
                    {
                        return (TObject)reader[0];
                    }
                    else
                    {
                        return (TObject)Convert.ChangeType(reader[0], typeof(TObject));
                    }
                }
                else
                {
                    return default(TObject);
                }
            }
            else
            {
                //dynamic
                if (typeof(TObject) == typeof(object))
                {
                    var dict = new Dictionary<string, object>();
                    SetDictionaryValues(reader, dict);
                    if (dict.Count() == 1)
                    {
                        return (TObject)dict.FirstOrDefault().Value;
                    }
                    var eo = new ExpandoObject();
                    var eoColl = (ICollection<KeyValuePair<string, object>>)eo;
                    foreach (var kvp in dict)
                    {
                        eoColl.Add(kvp);
                    }
                    dynamic eoDynamic = eo;
                    return eoDynamic;
                }
                else
                {
                    TObject entity = ((Func<TObject>)Caches.ConstructorCache.Get(typeof(TObject)))();
                    SetEntityMembers<TObject>(reader, entity);
                    return entity;
                }
            }
        }


        /// <summary>
        /// 常规反射
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="reader"></param>
        /// <param name="entity"></param>
        /// <param name="properties"></param>
        protected void SetEntityMembers1<T>(IDataReader reader, T entity)
        {
            int count = reader.FieldCount;
            for (int i = 0; i < count; i++)
            {
                PropertyInfo property;
                if (reader[i] != DBNull.Value && (property = typeof(T).GetProperty(reader.GetName(i), BindFlag)) != null)
                {
                    if (!property.PropertyType.IsGenericType)
                    {
                        property.SetValue(entity, Convert.ChangeType(reader[property.Name], property.PropertyType), null);
                    }
                    else
                    {
                        Type genericTypeDefinition = property.PropertyType.GetGenericTypeDefinition();
                        if (genericTypeDefinition == typeof(Nullable<>))
                        {
                            property.SetValue(entity, Convert.ChangeType(reader[property.Name], Nullable.GetUnderlyingType(property.PropertyType)), null);
                        }
                    }
                }
            }
        }

        #endregion

        /// <summary>
        /// Dispose
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing"></param>
        public virtual void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    this.DataBase.Dispose();
                }
            }
            this.disposed = true;
        }

        private bool disposed;
    }
}
