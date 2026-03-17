using System.Collections.Generic;

namespace AiApi.Models
{
    /// <summary>
    /// 浏览器执行请求
    /// </summary>
    public class BrowserRunRequest
    {
        /// <summary>
        /// 可选 SessionId
        /// 如果不传，会自动创建新的 Session
        /// </summary>
        public string? SessionId { get; set; }

        /// <summary>
        /// 浏览器动作列表
        /// </summary>
        public List<BrowserAction> Actions { get; set; } = new();

        /// <summary>
        /// 本次执行结束后是否关闭 Session
        /// </summary>
        public bool CloseSession { get; set; } = false;
    }

    /// <summary>
    /// Session操作请求
    /// </summary>
    public class BrowserSessionRequest
    {
        public string? SessionId { get; set; }
    }

    /// <summary>
    /// 浏览器动作
    /// </summary>
    public class BrowserAction
    {
        /// <summary>
        /// 动作类型
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// URL（goto / new_tab）
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// CSS选择器
        /// </summary>
        public string? Selector { get; set; }

        /// <summary>
        /// 通用值
        /// fill / press / click_text / check_text / switch_tab 等使用
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// JavaScript脚本
        /// evaluate 使用
        /// </summary>
        public string? Script { get; set; }

        /// <summary>
        /// wait 使用
        /// </summary>
        public double? Seconds { get; set; }

        /// <summary>
        /// 列表获取条数限制
        /// </summary>
        public int? Take { get; set; }

        /// <summary>
        /// 属性名
        /// </summary>
        public string? Attr { get; set; }

        /// <summary>
        /// 如果属性为空是否回退到 innerText
        /// </summary>
        public bool FallbackToInnerText { get; set; } = true;

        /// <summary>
        /// 等待超时时间（毫秒）
        /// </summary>
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// 最大滚动次数
        /// </summary>
        public int? MaxScrollCount { get; set; }

        /// <summary>
        /// 出错处理策略：stop / skip
        /// </summary>
        public string? OnError { get; set; } = "stop";

        /// <summary>
        /// 登录用户名
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// 登录密码
        /// </summary>
        public string? Password { get; set; }

        /// <summary>
        /// 是否等待点击后的页面稳定
        /// 很适合 click / click_text / click_index
        /// </summary>
        public bool WaitForNavigation { get; set; } = false;

        /// <summary>
        /// 点击后等待的 load state
        /// load / domcontentloaded / networkidle
        /// </summary>
        public string? WaitUntil { get; set; }

        /// <summary>
        /// 元素等待状态
        /// attached / detached / visible / hidden
        /// 供 wait_for / exists 等使用
        /// </summary>
        public string? WaitForState { get; set; }

        /// <summary>
        /// 索引
        /// 替代原来 click_index 用 value 传数字的方式，更清晰
        /// </summary>
        public int? Index { get; set; }

        /// <summary>
        /// select_option 时可用
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// 下载/截图等场景的文件名提示，可选
        /// </summary>
        public string? FileName { get; set; }
    }

    /// <summary>
    /// 单个动作结果
    /// </summary>
    public class BrowserActionResult
    {
        public bool Success { get; set; } = true;

        /// <summary>
        /// 结果类型，如 text / text_list / article / screenshot
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// 便于 AI 直接消费的文本结果
        /// </summary>
        public string? Text { get; set; }

        /// <summary>
        /// 便于 AI / Coze 直接取数组
        /// </summary>
        public List<string> List { get; set; } = new();

        /// <summary>
        /// 原始结构化数据
        /// </summary>
        public object? Data { get; set; }

        /// <summary>
        /// 可选标题
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 可选当前页面 / 目标页面 URL
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// 可选数量
        /// </summary>
        public int? Count { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// 附加元信息
        /// </summary>
        public Dictionary<string, object?> Meta { get; set; } = new();
    }

    /// <summary>
    /// 最终结果
    /// </summary>
    public class BrowserFinalResult
    {
        public string FinalType { get; set; } = "";
        public string FinalText { get; set; } = "";
        public string FinalDataJson { get; set; } = "[]";
        public List<string> FinalList { get; set; } = new();
        public object? FinalData { get; set; }
    }
}
