using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace WebApi
{
    /// <summary>
    /// 过滤有害sql，被AntiSqlInjectFilter调用。
    /// </summary>
    public class AntiSqlInject
    {
        public static AntiSqlInject Instance = new AntiSqlInject();

        /// <summary>
        /// 初始化过滤方法
        /// </summary>
        static AntiSqlInject()
        {
            SqlKeywordsArray.AddRange(SqlSeparatKeywords.Split('|'));
            SqlKeywordsArray.AddRange(Array.ConvertAll(SqlCommandKeywords.Split('|'), h => h + " "));
            SqlKeywordsArray.AddRange(Array.ConvertAll(SqlCommandKeywords.Split('|'), h => " " + h));
        }

        private const string SqlCommandKeywords = "and|exec|execute|insert|select|delete|update|count|chr|mid|master|" +
                                                  "char|declare|sitename|net user|xp_cmdshell|or|create|drop|table|from|grant|use|group_concat|column_name|" +
                                                  "information_schema.columns|table_schema|union|where|select|delete|update|orderhaving|having|by|count|*|truncate|like|drop";

        private const string SqlSeparatKeywords = "'|;|--|\'|\"|/*|%|#";

        private static readonly List<string> SqlKeywordsArray = new List<string>();

        /// <summary>
        /// 是否安全
        /// </summary>
        /// <param name="input">输入</param>
        /// <returns>返回</returns>
        public bool IsSafetySql(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return true;
            }
            input = HttpUtility.UrlDecode(input).ToLower();

            foreach (var sqlKeyword in SqlKeywordsArray)
            {
                if (input.IndexOf(sqlKeyword, StringComparison.Ordinal) >= 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 返回安全字符串
        /// </summary>
        /// <param name="input">输入</param>
        /// <returns>返回</returns>
        public string GetSafetySql(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            if (IsSafetySql(input)) { return input; }
            input = HttpUtility.UrlDecode(input).ToLower();

            foreach (var sqlKeyword in SqlKeywordsArray)
            {
                if (input.IndexOf(sqlKeyword, StringComparison.Ordinal) >= 0)
                {
                    input = input.Replace(sqlKeyword, string.Empty);
                }
            }
            return input;
        }
    }
}
