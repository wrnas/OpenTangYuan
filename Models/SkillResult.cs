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
    }

}
