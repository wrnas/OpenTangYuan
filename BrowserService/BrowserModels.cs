using System.Collections.Generic;

namespace AiApi.Models
{
    /// <summary>
    /// 浏览器执行请求
    /// 用于 /Browser/run 接口
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
    /// 用于关闭 Session
    /// </summary>
    public class BrowserSessionRequest
    {
        /// <summary>
        /// SessionId
        /// </summary>
        public string? SessionId { get; set; }
    }

    /// <summary>
    /// 浏览器动作
    /// AI 或 Swagger 发送的浏览器操作指令
    /// </summary>
    public class BrowserAction
    {
        /// <summary>
        /// 动作类型
        /// 例如：
        /// goto
        /// click
        /// fill
        /// wait
        /// evaluate
        /// get_text
        /// get_attr_list
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// URL
        /// goto / new_tab 使用
        /// </summary>
        public string? Url { get; set; }

        /// <summary>
        /// CSS选择器
        /// 例如：
        /// #UserUid
        /// .login-btn
        /// tr.new
        /// </summary>
        public string? Selector { get; set; }

        /// <summary>
        /// 通用值
        /// 用于：
        /// fill
        /// press
        /// click_text
        /// check_text
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// JavaScript脚本
        /// evaluate 使用
        /// </summary>
        public string? Script { get; set; }

        /// <summary>
        /// 等待秒数
        /// wait 使用
        /// </summary>
        public double? Seconds { get; set; }

        /// <summary>
        /// 列表获取条数限制
        /// get_text_list / get_attr_list 使用
        /// </summary>
        public int? Take { get; set; }

        /// <summary>
        /// 属性名
        /// get_attr / get_attr_list 使用
        /// 例如：
        /// title
        /// href
        /// src
        /// </summary>
        public string? Attr { get; set; }

        /// <summary>
        /// 如果属性为空是否回退到 innerText
        /// </summary>
        public bool FallbackToInnerText { get; set; } = true;

        /// <summary>
        /// 等待超时时间（毫秒）
        /// wait_for 使用
        /// </summary>
        public int? TimeoutMs { get; set; }

        /// <summary>
        /// 最大滚动次数
        /// scroll_until_text 使用
        /// </summary>
        public int? MaxScrollCount { get; set; }

        /// <summary>
        /// 出错处理策略
        /// stop = 停止执行
        /// skip = 跳过当前动作继续
        /// </summary>
        public string? OnError { get; set; } = "stop";
    }
}
