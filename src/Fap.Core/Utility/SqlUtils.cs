﻿using Fap.Core.DataAccess;
using Fap.Core.Exceptions;
using Fap.Core.Extensions;
using Fap.Core.Infrastructure.Metadata;
using Fap.Core.Infrastructure.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Fap.Core.Utility
{
    public class SqlUtils
    {
        public const string MatchBigParantheses = @"\{(.*?)\}";
        /// <summary>
        /// 返回sql where
        /// </summary>
        /// <param name="cols"></param>
        /// <param name="sqlDesc"></param>
        /// <returns></returns>
        public static string ParsingSql(IEnumerable<FapColumn> cols, string sqlDesc, DatabaseDialectEnum dialect)
        {
            Regex rgx = new Regex(MatchBigParantheses);
            MatchCollection matchs = rgx.Matches(sqlDesc);
            foreach (var mtch in matchs)
            {
                var colLabel = mtch.ToString().TrimStart('{').TrimEnd('}').Trim();
                sqlDesc = sqlDesc.ReplaceIgnoreCase(mtch.ToString(), cols.FirstOrDefault(c => c.ColComment == colLabel)?.ColName ?? "");
            }
            sqlDesc = ReplaceFunc(sqlDesc, dialect);
            sqlDesc = ReplaceConstant(sqlDesc);
            return sqlDesc;
        }
        private static string ReplaceFunc(string sqlDesc, DatabaseDialectEnum dialect)
        {
            if (dialect == DatabaseDialectEnum.MSSQL)
            {
                sqlDesc = sqlDesc
                .ReplaceIgnoreCase("[小时](", " DATEDIFF(hh,")
                .ReplaceIgnoreCase("[天](", " DATEDIFF(dd,")
                .ReplaceIgnoreCase("[星期](", " TimeStampDiff(wk,")
                .ReplaceIgnoreCase("[月](", " TimeStampDiff(mm,")
                .ReplaceIgnoreCase("[季度](", " TimeStampDiff(qq,")
                .ReplaceIgnoreCase("[年](", " DATEDIFF(yy,")
                .ReplaceIgnoreCase("[空](", " ISNULL(")
                ;
            }
            else if (dialect == DatabaseDialectEnum.MYSQL)
            {
                sqlDesc = sqlDesc
              .ReplaceIgnoreCase("[小时](", " TimeStampDiff(HOUR,")
              .ReplaceIgnoreCase("[天](", " TimeStampDiff(DAY,")
              .ReplaceIgnoreCase("[星期](", " TimeStampDiff(WEEK,")
              .ReplaceIgnoreCase("[月](", " TimeStampDiff(MONTH,")
              .ReplaceIgnoreCase("[季度](", "TimeStampDiff(QUARTER,")
              .ReplaceIgnoreCase("[年](", " TimeStampDiff(YEAR,")
              .ReplaceIgnoreCase("[空](", " IFNULL(");
            }
            sqlDesc = sqlDesc.ReplaceIgnoreCase("[绝对值](", " ABS(")
                .ReplaceIgnoreCase("[向上取整](", " CEILING(")
                .ReplaceIgnoreCase("[向下取整](", " FLOOR(")
                .ReplaceIgnoreCase("[四舍五入](", " ROUND(");
            return sqlDesc;
        }
        private static string ReplaceConstant(string sqlDesc)
        {
            return sqlDesc.ReplaceIgnoreCase("[当前日期]", $"'{DateTimeUtils.CurrentDateStr}'")
                .ReplaceIgnoreCase("[多条件赋值]", " case ")
                .ReplaceIgnoreCase("[当]", " when ")
                .ReplaceIgnoreCase("[取]", " then ")
                .ReplaceIgnoreCase("[否则]", " else ")
                .ReplaceIgnoreCase("[结束]", " end ")
                .ReplaceIgnoreCase("[条件]", " where ")
                .ReplaceIgnoreCase("[或]", " or ")
                .ReplaceIgnoreCase("[且]", " and ")
                ;
        }
        /// <summary>
        /// 返回数据sql
        /// </summary>
        /// <param name="cols"></param>
        /// <param name="data"></param>
        /// <param name="sqlDesc"></param>
        /// <param name="dialect"></param>
        /// <returns></returns>
        public static string ParsingConditionSql(IEnumerable<FapColumn> cols, IDictionary<string, object> data, string sqlDesc, DatabaseDialectEnum dialect)
        {
            Regex rgx = new Regex(MatchBigParantheses);
            MatchCollection matchs = rgx.Matches(sqlDesc);
            foreach (var mtch in matchs)
            {
                var colLabel = mtch.ToString().TrimStart('{').TrimEnd('}').Trim();
                FapColumn fcol = cols.FirstOrDefault(c => c.ColComment == colLabel);
                if (fcol != null)
                {
                    if (fcol.ColType == FapColumn.COL_TYPE_INT ||
                        fcol.ColType == FapColumn.COL_TYPE_LONG ||
                        fcol.ColType == FapColumn.COL_TYPE_DOUBLE ||
                        fcol.ColType == FapColumn.COL_TYPE_BOOL)
                    {
                        sqlDesc = sqlDesc.Replace(mtch.ToString(), data[fcol.ColName].ToStringOrEmpty());
                    }
                    else
                    {
                        sqlDesc = sqlDesc.Replace(mtch.ToString(), $"'{data[fcol.ColName].ToStringOrEmpty()}'");
                    }
                }
            }
            sqlDesc = ReplaceFunc(sqlDesc, dialect);
            sqlDesc = ReplaceConstant(sqlDesc);
            string sql = $"select 1 where {sqlDesc}";
            if (dialect != DatabaseDialectEnum.MSSQL)
            {
                sql = $"select 1 from dual where {sqlDesc}";
            }
            return sql;

        }
        public static string ParsingFormulaVariableSql(IEnumerable<FapColumn> cols,string colName, string sqlDesc, DatabaseDialectEnum dialect)
        {
            string tableName = cols.First().TableName;
            Regex rgx = new Regex(MatchBigParantheses);
            MatchCollection matchs = rgx.Matches(sqlDesc);
            foreach (var mtch in matchs)
            {
                var colLabel = mtch.ToString().TrimStart('{').TrimEnd('}').Trim();
                FapColumn fcol = cols.FirstOrDefault(c => c.ColComment == colLabel);
                if (fcol != null)
                {
                    sqlDesc = sqlDesc.Replace(mtch.ToString(), fcol.ColName);
                }
            }
            sqlDesc = ReplaceFunc(sqlDesc, dialect);
            sqlDesc = ReplaceConstant(sqlDesc);
            return $"update {tableName} set {colName}={sqlDesc}";
        }
        public static string ParsingFormulaMappingSql(IEnumerable<CfgEntityMapping> entityMappingList, string tableName, string colName, string sqlDesc, IDbContext dbContext)
        {
            sqlDesc = sqlDesc.ReplaceIgnoreCase("[引用]", "").TrimStart('(').TrimEnd(')');
            string aimsTable = sqlDesc.Substring(0, sqlDesc.IndexOf("["));
            string field = sqlDesc.Substring(sqlDesc.IndexOf("[") + 1).TrimEnd(']');
            var entityMapping = entityMappingList.FirstOrDefault(m => m.AimsEntityMC == aimsTable);
            if (entityMapping != null)
            {
                var fieldCol = dbContext.Columns(entityMapping.AimsEntity).FirstOrDefault(c => c.ColComment == field);
                IEnumerable<Associate> associates = JsonConvert.DeserializeObject<IEnumerable<Associate>>(entityMapping.Associate);
                List<string> where = new List<string>();
                foreach (var associate in associates)
                {
                    where.Add($"{tableName}.{associate.OriCol}={entityMapping.AimsEntity}.{associate.AimsCol}");
                }
                string innerJoin = $" inner join  {entityMapping.AimsEntity} on {string.Join(" and ", where)}";
                if (dbContext.DatabaseDialect == DatabaseDialectEnum.MSSQL)
                {
                    return $"update {tableName} set {tableName}.{colName}={entityMapping.AimsEntity}.{fieldCol.ColName} from {tableName} {innerJoin}";
                }
                else if (dbContext.DatabaseDialect == DatabaseDialectEnum.MYSQL)
                {
                    return $"update {tableName} {innerJoin} set {tableName}.{colName}={entityMapping.AimsEntity}.{fieldCol.ColName}";
                }
                return string.Empty;
            }
            return string.Empty;
        }
        public static string ParsingFormulaCheckSql(string tableName, string checkSql, DatabaseDialectEnum dialect)
        {
            if (dialect == DatabaseDialectEnum.MSSQL)
            {
                string sql = checkSql.ReplaceIgnoreCase("tableName", "#FmuValideTemp");
                return string.Format(@"
                if exists(select * from tempdb..sysobjects where id=object_id('tempdb..#FmuValideTemp'))
                begin
                drop table #FmuValideTemp
                end
                select top 1 * into #FmuValideTemp from {0} where 1 = 2;
                {1};
                drop table #FmuValideTemp;", tableName,sql);
            }
            else if (dialect == DatabaseDialectEnum.MYSQL)
            {
                string sql = checkSql.ReplaceIgnoreCase("tableName", "FmuValideTemp");
                return string.Format(@"CREATE TEMPORARY TABLE FmuValideTemp(SELECT  * FROM {0} where 1=2 ); 
{1};
drop table FmuValideTemp;", tableName, sql);
            }
            return string.Empty;

        }
    }
}
