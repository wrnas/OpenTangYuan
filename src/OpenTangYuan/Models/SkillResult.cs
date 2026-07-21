using System.Reflection;
using System.Text.Json;

namespace TangYuan.Models
{
    /// <summary>
    /// 技能统一返回结果
    ///
    /// 设计说明：
    /// 1. Text / Data / Error 是原始结构，兼容现有代码
    /// 2. ResultText / ResultList / ResultValue 是给 AI / Coze 使用的扁平字段
    /// 3. 推荐在返回前调用 Normalize()，自动补齐常用字段，减少遗漏
    /// </summary>
    public class SkillResult
    {
        public bool Success { get; set; } = true;
        public string SkillCode { get; set; } = "";
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public object? Data { get; set; }
        public string Error { get; set; } = "";

        /// <summary>
        /// 扁平文本结果，主要给 AI / Coze 读取
        /// </summary>
        public string ResultText { get; set; } = "";

        /// <summary>
        /// 扁平列表结果，适合文件搜索、文本列表、链接列表等场景
        /// </summary>
        public List<string> ResultList { get; set; } = new();

        /// <summary>
        /// 扁平单值结果，适合文件路径、截图路径、下载路径、第一条结果等场景
        /// </summary>
        public string ResultValue { get; set; } = "";

        /// <summary>
        /// 明确设置单个关键结果值
        /// </summary>
        public SkillResult WithValue(string? value)
        {
            ResultValue = value?.Trim() ?? "";

            if (!string.IsNullOrWhiteSpace(ResultValue) && (ResultList == null || ResultList.Count == 0))
            {
                ResultList = new List<string> { ResultValue };
            }

            return this;
        }

        /// <summary>
        /// 明确设置列表结果
        /// </summary>
        public SkillResult WithList(IEnumerable<string>? values)
        {
            ResultList = values?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            if (string.IsNullOrWhiteSpace(ResultValue) && ResultList.Count > 0)
            {
                ResultValue = ResultList[0];
            }

            return this;
        }

        /// <summary>
        /// 自动补齐扁平字段，建议在返回前统一调用
        ///
        /// 规则：
        /// 1. ResultText 为空时，优先使用 Error（失败时）或 Text（成功时）
        /// 2. 如果 ResultList / ResultValue 为空，则尝试从 Data 中自动提取常见字段：
        ///    - 列表字段：list / paths / items
        ///    - 单值字段：firstPath / path
        ///    - 嵌套字段：result.path
        /// 3. 如果 ResultValue 仍为空且 ResultList 有值，则使用第一项
        ///
        /// 说明：
        /// - 不会覆盖你手工设置好的 ResultText / ResultList / ResultValue
        /// - 适合绝大多数场景做统一收口
        /// - 对特别明确的场景，仍建议配合 WithValue / WithList 使用
        /// </summary>
        public SkillResult Normalize()
        {
            if (string.IsNullOrWhiteSpace(ResultText))
            {
                if (!Success && !string.IsNullOrWhiteSpace(Error))
                    ResultText = Error;
                else
                    ResultText = Text ?? "";
            }

            // 如果还没有列表，尝试从 Data 中提取常见列表字段
            if ((ResultList == null || ResultList.Count == 0) && Data != null)
            {
                var list = TryGetStringListFromData(Data, "list")
                           ?? TryGetStringListFromData(Data, "paths")
                           ?? TryGetStringListFromData(Data, "items");

                if (list != null && list.Count > 0)
                {
                    ResultList = list;
                }
            }

            // 如果还没有单值，尝试从 Data 中提取常见字段
            if (string.IsNullOrWhiteSpace(ResultValue) && Data != null)
            {
                ResultValue =
                    TryGetStringFromData(Data, "firstPath") ??
                    TryGetStringFromData(Data, "path") ??
                    TryGetNestedStringFromData(Data, "result", "path") ??
                    "";
            }

            // 最后再从列表兜底
            if (string.IsNullOrWhiteSpace(ResultValue) && ResultList != null && ResultList.Count > 0)
            {
                ResultValue = ResultList[0];
            }

            return this;
        }

        private static List<string>? TryGetStringListFromData(object data, string propertyName)
        {
            var value = TryGetPropertyValue(data, propertyName);
            if (value == null) return null;

            if (value is List<string> list)
                return list;

            if (value is IEnumerable<string> strEnum)
                return strEnum.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    return je.EnumerateArray()
                        .Select(x => x.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                }

                if (je.ValueKind == JsonValueKind.String)
                {
                    var s = je.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return new List<string> { s };
                }
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                var result = new List<string>();
                foreach (var item in enumerable)
                {
                    var s = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        result.Add(s);
                }
                return result.Count > 0 ? result : null;
            }

            var single = value.ToString();
            if (!string.IsNullOrWhiteSpace(single))
                return new List<string> { single };

            return null;
        }

        private static string? TryGetStringFromData(object data, string propertyName)
        {
            var value = TryGetPropertyValue(data, propertyName);
            return ConvertObjectToString(value);
        }

        private static string? TryGetNestedStringFromData(object data, string parentName, string childName)
        {
            var parent = TryGetPropertyValue(data, parentName);
            if (parent == null) return null;

            var child = TryGetPropertyValue(parent, childName);
            return ConvertObjectToString(child);
        }

        private static object? TryGetPropertyValue(object obj, string propertyName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty(propertyName, out var child))
                        return child;

                    foreach (var jsonProp in je.EnumerateObject())
                    {
                        if (string.Equals(jsonProp.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                            return jsonProp.Value;
                    }
                }

                return null;
            }

            if (obj is IDictionary<string, object> dict)
            {
                if (dict.TryGetValue(propertyName, out var value))
                    return value;

                var match = dict.Keys.FirstOrDefault(k =>
                    string.Equals(k, propertyName, StringComparison.OrdinalIgnoreCase));

                if (match != null && dict.TryGetValue(match, out var matched))
                    return matched;

                return null;
            }

            var prop = obj.GetType().GetProperty(
                propertyName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            return prop?.GetValue(obj);
        }


        private static string? ConvertObjectToString(object? value)
        {
            if (value == null) return null;

            return value switch
            {
                string s => string.IsNullOrWhiteSpace(s) ? null : s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                JsonElement je => je.ToString(),
                _ => value.ToString()
            };
        }
    }
}
