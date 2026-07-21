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
        public string? Type { get; set; }

        public string? Url { get; set; }

        public string? Selector { get; set; }

        public string? Value { get; set; }

        public string? Script { get; set; }

        public double? Seconds { get; set; }

        public int? Take { get; set; }

        public string? Attr { get; set; }

        public bool FallbackToInnerText { get; set; } = true;

        public int? TimeoutMs { get; set; }

        public int? MaxScrollCount { get; set; }

        public string? OnError { get; set; } = "stop";

        public string? Username { get; set; }

        public string? Password { get; set; }

        public bool WaitForNavigation { get; set; } = false;

        public string? WaitUntil { get; set; }

        public string? WaitForState { get; set; }

        public int? Index { get; set; }

        public string? Label { get; set; }

        public string? FileName { get; set; }

        /// <summary>
        /// 本地文件路径，适用于 screenshot 等动作
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 是否整页截图，适用于 screenshot
        /// </summary>
        public bool? FullPage { get; set; }
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




    public class BrowserPageState
    {
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
    }

    public class BrowserSessionState
    {
        public string SessionId { get; set; } = "";
        public bool Reusable { get; set; }
        public bool KeepAliveSuggested { get; set; }
        public int TimeoutMinutes { get; set; } = 30;
        public string FollowUpHint { get; set; } = "";
    }

    public class WorkflowSkillResult
    {
        public bool Success { get; set; } = true;
        public string SkillCode { get; set; } = "";
        public string Type { get; set; } = "workflow";
        public string Text { get; set; } = "";
        public object? Data { get; set; }
        public string Error { get; set; } = "";
    }

    public class CozeSkillResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string SkillCode { get; set; } = "";
        public string ExecuteMode { get; set; } = "";
        public string ResultType { get; set; } = "";
        public string ResultText { get; set; } = "";
        public List<string> ResultList { get; set; } = new();
        public string ResultValue { get; set; } = "";
        public object? ResultData { get; set; }
        public string SessionId { get; set; } = "";
        public BrowserPageState? Page { get; set; }
        public BrowserSessionState? Session { get; set; }
        public bool NeedMoreInput { get; set; }
        public List<string> MissingArgs { get; set; } = new();
        public string ErrorCode { get; set; } = "";
        public string ErrorMessage { get; set; } = "";

        public static CozeSkillResponse Fail(
            string message,
            string skillCode,
            string executeMode,
            string errorCode,
            string errorMessage,
            bool needMoreInput = false,
            List<string>? missingArgs = null)
        {
            return new CozeSkillResponse
            {
                Success = false,
                Message = message,
                SkillCode = skillCode,
                ExecuteMode = executeMode,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                NeedMoreInput = needMoreInput,
                MissingArgs = missingArgs ?? new List<string>(),
                ResultText = errorMessage
            };
        }
    }



}
