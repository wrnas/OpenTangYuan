using AiApi.Models;
using AiApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using System.Collections;

namespace AiApi.Controllers
{
    /// <summary>
    /// Browser agent controller (production-enhanced version)
    ///
    /// Design goals:
    /// 1. Control the browser through JSON actions (compatible with Coze / AI)
    /// 2. Return a stable structure that is easy for large language models to parse
    /// 3. Each action is a composable atomic capability
    ///
    /// ⚠️ Notes:
    /// - CAPTCHA solving is not automated; only detection and human-assisted handling are supported
    /// - evaluate poses security risks and should be restricted in production
    /// - Sessions must be properly cleaned up (otherwise browser processes will accumulate)
    ///
    /// ⭐ Recommended usage (Coze)
    /// - Call only /run
    /// - Read result.text / result.list / result.data
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
                message = "Session created successfully"
            });
        }


        /// <summary>
        /// Execute a sequence of browser actions (core endpoint)
        ///
        /// This is the only endpoint recommended for Coze
        ///
        /// Request structure:
        /// {
        ///   "actions": [
        ///     { "type": "goto", "url": "https://xxx.com" },
        ///     { "type": "get_text_list", "selector": ".item" }
        ///   ]
        /// }
        ///
        /// Key response fields:
        /// - result.text   👉 Directly readable by AI
        /// - result.list   👉 Used by AI for iteration
        /// - result.data   👉 Raw structured data
        ///
        /// ⚠️ Notes:
        /// - actions is required
        /// - The session is created or reused automatically
        /// - Actions within the same session are executed sequentially (to prevent concurrency issues)
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
                    error = "actions cannot be empty"
                });
            }

            BrowserSession? session = null;

            try
            {
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = _browserService.GetSession(request.SessionId);
                }

                if (session == null)
                {
                    session = await _browserService.CreateSessionAsync();
                }

                var outputs = new List<BrowserActionResult>();
                var logs = new List<object>();

                await session.ActionLock.WaitAsync();

                try
                {
                    foreach (var action in request.Actions)
                    {
                        var startTime = DateTime.UtcNow;

                        try
                        {
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
                            _logger.LogError(ex, "Action execution failed");

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

                    result = new
                    {
                        type = final.FinalType,
                        text = final.FinalText,
                        list = final.FinalList,
                        data = final.FinalData
                    },

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
            finally
            {
                if (request.CloseSession && session != null)
                {
                    try
                    {
                        await _browserService.CloseSession(session.SessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to close browser session, SessionId={SessionId}", session.SessionId);
                    }
                }
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
                    error = "sessionId cannot be empty"
                });
            }

            await _browserService.CloseSession(request.SessionId);

            return Ok(new
            {
                success = true,
                message = "Session closed"
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
