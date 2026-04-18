namespace TangYuan.Models
{
    public class SkillResult
    {
        public bool Success { get; set; } = true;
        public string SkillCode { get; set; } = "";
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public object? Data { get; set; }
        public string Error { get; set; } = "";

        // 新增：兼容智能体 / Coze 的扁平字段
        public string ResultText { get; set; } = "";
        public List<string> ResultList { get; set; } = new();
        public string ResultValue { get; set; } = "";
    }
}
