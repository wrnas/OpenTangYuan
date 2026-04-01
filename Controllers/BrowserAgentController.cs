using AiApi.Models;
using AiApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using System.Collections;

namespace AiApi.Controllers
{
    /// <summary>
    /// 浏览器智能体控制器（生产增强版）
    ///
    /// 设计目标：
    /// 1. 通过 JSON actions 控制浏览器（适配 Coze / AI）
    /// 2. 返回结构稳定，便于大模型解析
    /// 3. 每个 action 都是“可组合的原子能力”
    ///
    /// ⚠️ 注意：
    /// - 不做验证码自动破解，只做检测 + 人工协作
    /// - evaluate 有安全风险，建议生产限制
    /// - session 必须做好回收（否则会堆积浏览器进程）
    ///
    /// ⭐ 推荐使用方式（Coze）
    /// - 只调用 /run
    /// - 读取 result.text / result.list / result.data
    /// </summary>

    [ApiController]
    [Route("AiApi/Browser")]
    public class BrowserAgentController : ControllerBase
    {
        private readonly BrowserService _browserService;
        private readonly ILogger<BrowserAgentController> _logger;
        private readonly IWebHostEnvironment _env;

        public BrowserAgentController(
            BrowserService browserService,
            ILogger<BrowserAgentController> logger,
            IWebHostEnvironment env)
        {
            _browserService = browserService;
            _logger = logger;
            _env = env;
        }

        #region Public API

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            var session = await _browserService.CreateSessionAsync();

            return Ok(new
            {
                success = true,
                sessionId = session.SessionId,
                message = "Session 创建成功"
            });
        }

        
        /// <summary>
        /// 执行浏览器动作序列（核心入口）
        ///
        /// 这是唯一推荐给 Coze 使用的接口
        ///
        /// 请求结构：
        /// {
        ///   "actions": [
        ///     { "type": "goto", "url": "https://xxx.com" },
        ///     { "type": "get_text_list", "selector": ".item" }
        ///   ]
        /// }
        ///
        /// 返回重点字段：
        /// - result.text   👉 给 AI 直接读
        /// - result.list   👉 给 AI 做循环
        /// - result.data   👉 原始结构化数据
        ///
        /// ⚠️ 注意：
        /// - actions 必须有
        /// - session 自动创建 / 复用
        /// - 同一 session 内部是串行执行（防并发问题）
        /// </summary>
        [HttpPost("run")]
        public async Task<IActionResult> Run([FromBody] BrowserRunRequest request)
        {
            request ??= new BrowserRunRequest();
            request.Actions ??= new List<BrowserAction>();

            if (request.Actions.Count == 0)
            {
                return Ok(new
                {
                    success = false,
                    error = "actions 不能为空"
                });
            }

            BrowserSession? session = null;

            try
            {
                // 1️⃣ 尝试复用 session
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = _browserService.GetSession(request.SessionId);
                }

                // 2️⃣ 不存在就创建
                if (session == null)
                {
                    session = await _browserService.CreateSessionAsync();
                }

                var outputs = new List<BrowserActionResult>();
                var logs = new List<object>();

                // ⭐ 关键：同一个 session 必须串行执行
                await session.ActionLock.WaitAsync();

                try
                {
                    foreach (var action in request.Actions)
                    {
                        var startTime = DateTime.UtcNow;

                        try
                        {
                            // ⭐ 执行单个 action
                            var result = await _browserService.ExecuteActionAsync(session, action);


                            outputs.Add(result);

                            logs.Add(new
                            {
                                action = action.Type,
                                success = true,
                                durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Action 执行失败");

                            // skip = 跳过错误继续执行
                            if (string.Equals(action.OnError, "skip", StringComparison.OrdinalIgnoreCase))
                            {
                                outputs.Add(new BrowserActionResult
                                {
                                    Success = false,
                                    Type = "error",
                                    Text = ex.Message,
                                    Error = ex.Message
                                });

                                continue;
                            }

                            // stop = 直接返回
                            return Ok(new
                            {
                                success = false,
                                sessionId = session.SessionId,
                                error = ex.Message,
                                outputs,
                                logs
                            });
                        }
                    }
                }
                finally
                {
                    session.ActionLock.Release();
                }

                // ⭐ 核心：统一提取最终结果（给 AI 用）
                var final = _browserService.BuildFinalResult(outputs);

                return Ok(new
                {
                    success = true,
                    sessionId = session.SessionId,

                    page = new
                    {
                        url = session.CurrentPage.Url,
                        title = await _browserService.SafeGetTitleAsync(session.CurrentPage)
                    },

                    // ⭐⭐⭐ Coze 最重要的字段
                    result = new
                    {
                        type = final.FinalType,
                        text = final.FinalText,
                        list = final.FinalList,
                        data = final.FinalData
                    },

                    // 调试用
                    outputs,
                    logs
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    error = ex.Message
                });
            }
        }



        [HttpPost("close")]
        public async Task<IActionResult> Close([FromBody] BrowserSessionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SessionId))
            {
                return Ok(new
                {
                    success = false,
                    error = "sessionId 不能为空"
                });
            }

            await _browserService.CloseSession(request.SessionId);

            return Ok(new
            {
                success = true,
                message = "Session 已关闭"
            });
        }

        [HttpGet("sessions")]
        public IActionResult Sessions()
        {
            return Ok(new
            {
                success = true,
                sessions = _browserService.GetSessions()
            });
        }

        #endregion

        
    }
}
