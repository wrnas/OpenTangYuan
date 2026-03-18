using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using TangYuan.Tools;

namespace TangYuan.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SendMessageCtrlController : ControllerBase
    {
        [HttpPost("SendToDingdingText")]
        public async Task<IActionResult> SendToDingdingText(string webhook,string msg)
        {
            // 你的钉钉机器人Webhook
            if (webhook.IsNullOrEmpty())
                webhook = "https://oapi.dingtalk.com/robot/send?access_token=xxxx";

            // 发送消息
            var res = await DingTalkBotHelper.SendText(webhook, msg);

            return Ok(new { code = 200, data = res });

        }

        /// <summary>
        /// 发送文本消息到团队群
        /// </summary>
        [HttpPost("SendText")]
        public async Task<IActionResult> SendText(string msg, bool atAll = false)
        {
            var res = await WechatBotHelper.SendText(msg, atAll);
            return Ok(new { code = 200, data = res });
        }

        /// <summary>
        /// 发送Markdown格式消息
        /// </summary>
        [HttpPost("SendMarkdown")]
        public async Task<IActionResult> SendMarkdown(string msg)
        {
            var res = await WechatBotHelper.SendMarkdown(msg);
            return Ok(new { code = 200, data = res });
        }

        /// <summary>
        /// 发送卡片消息
        /// </summary>
        [HttpPost("SendCard")]
        public async Task<IActionResult> SendCard(string title, string desc, string url)
        {
            var res = await WechatBotHelper.SendCard(title, desc, url);
            return Ok(new { code = 200, data = res });
        }
    }
}
