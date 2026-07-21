using Microsoft.AspNetCore.Mvc;

namespace TangYuan.Tools
{
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;

    public static class DingTalkBotHelper
    {
        /// <summary>
        /// 发送钉钉群消息（支持在微信里查看！）
        /// </summary>
        public static async Task<string> SendText(string webhookUrl, string text)
        {
            try
            {
                var msg = new
                {
                    msgtype = "text",
                    text = new { content = text }
                };

                var json = JsonSerializer.Serialize(msg);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var client = new HttpClient();
                var resp = await client.PostAsync(webhookUrl, content);
                return await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return $"发送失败：{ex.Message}";
            }
        }
    }
}
