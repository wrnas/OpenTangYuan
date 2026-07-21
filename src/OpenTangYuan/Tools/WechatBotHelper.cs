namespace TangYuan.Tools
{
    using Microsoft.Extensions.Configuration;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// 微信团队群机器人通用发送工具
    /// 支持：文本、Markdown、卡片、@所有人、@指定人
    /// </summary>
    public static class WechatBotHelper
    {
        private static readonly IConfiguration _config;

        static WechatBotHelper()
        {
            _config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
        }

        #region 1. 发送纯文本消息
        public static async Task<string> SendText(string content, bool isAtAll = false, string[] atUsers = null)
        {
            var data = new
            {
                msgtype = "text",
                text = new
                {
                    content,
                    mentioned_list = isAtAll ? new[] { "@all" } : atUsers ?? Array.Empty<string>()
                }
            };
            return await SendToWebhook(data);
        }
        #endregion

        #region 2. 发送 Markdown 消息（支持加粗、换行、链接）
        public static async Task<string> SendMarkdown(string markdownContent)
        {
            var data = new
            {
                msgtype = "markdown",
                markdown = new { content = markdownContent }
            };
            return await SendToWebhook(data);
        }
        #endregion

        #region 3. 发送图文卡片消息（最常用、最美观）
        public static async Task<string> SendCard(string title, string desc, string url, string picUrl = "")
        {
            var data = new
            {
                msgtype = "news",
                news = new
                {
                    articles = new[]
                    {
                    new
                    {
                        title,
                        description = desc,
                        url,
                        picurl = picUrl
                    }
                }
                }
            };
            return await SendToWebhook(data);
        }
        #endregion

        #region 核心发送方法（统一请求）
        private static async Task<string> SendToWebhook(object data)
        {
            try
            {
                string webhook = _config["Wechat:TeamGroupWebhook"]?.Trim();
                if (string.IsNullOrEmpty(webhook))
                    return "错误：未配置群机器人Webhook";

                using var client = new HttpClient();
                string json = JsonSerializer.Serialize(data);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var res = await client.PostAsync(webhook, content);
                string result = await res.Content.ReadAsStringAsync();
                return $"成功：{result}";
            }
            catch (Exception ex)
            {
                return $"失败：{ex.Message}";
            }
        }
        #endregion
    }
}
