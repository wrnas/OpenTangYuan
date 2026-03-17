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

                // 建议：BrowserSession 中增加 ActionLock，防并发串台
                if (session.ActionLock != null)
                {
                    await session.ActionLock.WaitAsync();
                }

                try
                {
                    foreach (var action in request.Actions)
                    {
                        var startTime = DateTime.UtcNow;
                        var actionName = action.Type ?? "";

                        try
                        {
                            var result = await ExecuteAction(session, action);
                            outputs.Add(result);

                            logs.Add(new
                            {
                                action = actionName,
                                success = true,
                                durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "BrowserAction 执行失败: {ActionType}", actionName);

                            logs.Add(new
                            {
                                action = actionName,
                                success = false,
                                durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                                error = ex.Message
                            });

                            if (string.Equals(action.OnError, "skip", StringComparison.OrdinalIgnoreCase))
                            {
                                outputs.Add(new BrowserActionResult
                                {
                                    Success = false,
                                    Type = "error",
                                    Error = ex.Message,
                                    Text = ex.Message,
                                    Meta =
                                    {
                                        ["action"] = actionName
                                    }
                                });

                                continue;
                            }

                            return Ok(new
                            {
                                success = false,
                                sessionId = session.SessionId,
                                error = ex.Message,
                                errorDetail = ex.ToString(),
                                page = new
                                {
                                    url = session.CurrentPage?.Url ?? "",
                                    title = session.CurrentPage == null ? "" : await SafeGetTitleAsync(session.CurrentPage)
                                },
                                outputs,
                                logs
                            });
                        }
                    }
                }
                finally
                {
                    if (session.ActionLock != null)
                    {
                        session.ActionLock.Release();
                    }
                }

                var final = BuildFinalResult(outputs);

                var response = new
                {
                    success = true,
                    sessionId = session.SessionId,
                    page = new
                    {
                        url = session.CurrentPage.Url,
                        title = await SafeGetTitleAsync(session.CurrentPage)
                    },
                    result = new
                    {
                        type = final.FinalType,
                        text = final.FinalText,
                        list = final.FinalList,
                        dataJson = final.FinalDataJson,
                        data = final.FinalData,
                        count = final.FinalList.Count
                    },

                    // 兼容你原先的字段
                    finalType = final.FinalType,
                    finalText = final.FinalText,
                    finalDataJson = final.FinalDataJson,
                    finalList = final.FinalList,

                    outputs,
                    logs
                };

                if (request.CloseSession)
                {
                    await _browserService.CloseSession(session.SessionId);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Run 执行失败");

                return Ok(new
                {
                    success = false,
                    error = ex.Message,
                    errorDetail = ex.ToString()
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

        #region Core Action Executor

        private async Task<BrowserActionResult> ExecuteAction(BrowserSession session, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Type))
                throw new Exception("action.type 不能为空");

            var page = session.CurrentPage;
            var actionType = action.Type.Trim().ToLowerInvariant();
            var beforeUrl = page.Url;

            BrowserActionResult result = actionType switch
            {
                "goto" => await ExecuteGoto(page, action),
                "goto_attr" => await ExecuteGotoAttr(page, action),
                "back" => await ExecuteBack(page),
                "forward" => await ExecuteForward(page),
                "refresh" => await ExecuteRefresh(page),

                "click" => await ExecuteClick(page, action),
                "click_index" => await ExecuteClickIndex(page, action),
                "click_text" => await ExecuteClickText(page, action),
                "click_if_exists" => await ExecuteClickIfExists(page, action),
                "fill" => await ExecuteFill(page, action),
                "press" => await ExecutePress(page, action),
                "hover" => await ExecuteHover(page, action),
                "select_option" => await ExecuteSelectOption(page, action),

                "wait" => await ExecuteWait(action),
                "wait_for" => await ExecuteWaitFor(page, action),
                "wait_for_load_state" => await ExecuteWaitForLoadState(page, action),
                "wait_url_contains" => await ExecuteWaitUrlContains(page, action),

                "scroll_bottom" => await ExecuteScrollBottom(page),
                "scroll_until_text" => await ExecuteScrollUntilText(page, action),

                "get_text" => await ExecuteGetText(page, action),
                "get_text_list" => await ExecuteGetTextList(page, action),
                "get_attr" => await ExecuteGetAttr(page, action),
                "get_attr_list" => await ExecuteGetAttrList(page, action),
                "get_links" => await ExecuteGetLinks(page, action),
                "get_links_structured" => await ExecuteGetLinksStructured(page, action),
                "get_html" => await ExecuteGetHtml(page),
                "check_text" => await ExecuteCheckText(page, action),
                "exists" => await ExecuteExists(page, action),
                "count" => await ExecuteCount(page, action),

                "analyze_page" => await AnalyzePage(page),
                "extract_article" => await ExtractArticle(page),
                "extract_list" => await ExecuteExtractList(page, action),

                "screenshot" => await ExecuteScreenshot(page),
                "screenshot_base64" => await ExecuteScreenshotBase64(page),
                "screenshot_element" => await ExecuteScreenshotElement(page, action),
                "download" => await ExecuteDownload(page, action),

                "new_tab" => await ExecuteNewTab(session, action),
                "switch_tab" => await ExecuteSwitchTab(session, action),
                "get_tabs" => await ExecuteGetTabs(session),
                "clear_cookies" => await ExecuteClearCookies(session),

                "auto_detect_login" => await ExecuteAutoDetectLogin(page, action),
                "auto_login_form" => await ExecuteAnalyzeLoginForm(page),

                "evaluate" => await ExecuteEvaluate(page, action),

                "detect_captcha" => await ExecuteDetectCaptcha(page),
                "capture_captcha" => await ExecuteCaptureCaptcha(page, action),
                "submit_captcha" => await ExecuteSubmitCaptcha(page, action),

                _ => throw new Exception($"不支持的动作类型: {action.Type}")
            };

            // 二次校验：动作执行后如果 URL 变了，检查域名是否允许
            var afterUrl = session.CurrentPage.Url;
            if (!string.IsNullOrWhiteSpace(afterUrl) &&
                !string.Equals(beforeUrl, afterUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (!_browserService.IsAllowedDomain(afterUrl))
                    throw new Exception("跳转后的域名不允许");
            }

            if (string.IsNullOrWhiteSpace(result.Url))
                result.Url = session.CurrentPage.Url;

            if (string.IsNullOrWhiteSpace(result.Title))
                result.Title = await SafeGetTitleAsync(session.CurrentPage);

            return result;
        }

        #endregion

        #region Navigation

        private async Task<BrowserActionResult> ExecuteGoto(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Url))
                throw new Exception("goto 动作必须提供 url");

            if (!_browserService.IsAllowedDomain(action.Url))
                throw new Exception("访问域名不允许");

            await page.GotoAsync(action.Url, new PageGotoOptions
            {
                WaitUntil = ParseWaitUntilState(action.WaitUntil)
            });

            return new BrowserActionResult
            {
                Type = "goto",
                Url = page.Url,
                Title = await SafeGetTitleAsync(page),
                Text = page.Url
            };
        }

        private async Task<BrowserActionResult> ExecuteGotoAttr(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("goto_attr 动作必须提供 selector");

            var attr = string.IsNullOrWhiteSpace(action.Attr) ? "href" : action.Attr;
            var raw = await page.Locator(action.Selector).First.GetAttributeAsync(attr);

            if (string.IsNullOrWhiteSpace(raw))
                throw new Exception($"未找到属性 {attr}");

            var targetUrl = ToAbsoluteUrl(page.Url, raw);

            if (!_browserService.IsAllowedDomain(targetUrl))
                throw new Exception("访问域名不允许");

            await page.GotoAsync(targetUrl, new PageGotoOptions
            {
                WaitUntil = ParseWaitUntilState(action.WaitUntil)
            });

            return new BrowserActionResult
            {
                Type = "goto_attr",
                Url = targetUrl,
                Text = targetUrl,
                Meta =
                {
                    ["selector"] = action.Selector,
                    ["attr"] = attr
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteBack(IPage page)
        {
            await page.GoBackAsync();
            return new BrowserActionResult
            {
                Type = "back",
                Url = page.Url,
                Title = await SafeGetTitleAsync(page),
                Text = page.Url
            };
        }

        private async Task<BrowserActionResult> ExecuteForward(IPage page)
        {
            await page.GoForwardAsync();
            return new BrowserActionResult
            {
                Type = "forward",
                Url = page.Url,
                Title = await SafeGetTitleAsync(page),
                Text = page.Url
            };
        }

        private async Task<BrowserActionResult> ExecuteRefresh(IPage page)
        {
            await page.ReloadAsync();
            return new BrowserActionResult
            {
                Type = "refresh",
                Url = page.Url,
                Title = await SafeGetTitleAsync(page),
                Text = page.Url
            };
        }

        #endregion

        #region Interaction

        private async Task<BrowserActionResult> ExecuteClick(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("click 动作必须提供 selector");

            var locator = page.Locator(action.Selector).First;
            await ClickAndMaybeWait(page, locator, action);

            return new BrowserActionResult
            {
                Type = "click",
                Text = action.Selector,
                Meta =
                {
                    ["selector"] = action.Selector
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteClickIndex(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("click_index 动作必须提供 selector");

            var index = action.Index ?? 0;
            if (action.Index == null && !string.IsNullOrWhiteSpace(action.Value))
            {
                int.TryParse(action.Value, out index);
            }

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();

            if (index < 0 || index >= count)
                throw new Exception($"index 超出范围，当前元素数量: {count}");

            var target = loc.Nth(index);
            await ClickAndMaybeWait(page, target, action, force: true);

            return new BrowserActionResult
            {
                Type = "click_index",
                Text = $"{action.Selector}[{index}]",
                Meta =
                {
                    ["selector"] = action.Selector,
                    ["index"] = index
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteClickText(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("click_text 动作必须提供 value");

            var locator = page.GetByText(action.Value, new PageGetByTextOptions
            {
                Exact = false
            }).First;

            await ClickAndMaybeWait(page, locator, action);

            return new BrowserActionResult
            {
                Type = "click_text",
                Text = action.Value,
                Meta =
                {
                    ["text"] = action.Value
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteClickIfExists(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("click_if_exists 动作必须提供 selector");

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();

            if (count <= 0)
            {
                return new BrowserActionResult
                {
                    Type = "click_if_exists",
                    Text = "not_clicked",
                    Data = new
                    {
                        clicked = false,
                        selector = action.Selector
                    }
                };
            }

            await ClickAndMaybeWait(page, loc.First, action);

            return new BrowserActionResult
            {
                Type = "click_if_exists",
                Text = "clicked",
                Data = new
                {
                    clicked = true,
                    selector = action.Selector
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteFill(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("fill 动作必须提供 selector");

            await page.Locator(action.Selector).First.FillAsync(action.Value ?? string.Empty);

            return new BrowserActionResult
            {
                Type = "fill",
                Text = action.Value ?? "",
                Meta =
                {
                    ["selector"] = action.Selector
                }
            };
        }

        private async Task<BrowserActionResult> ExecutePress(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("press 动作必须提供 selector");

            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("press 动作必须提供 value");

            await page.Locator(action.Selector).First.PressAsync(action.Value);

            return new BrowserActionResult
            {
                Type = "press",
                Text = action.Value,
                Meta =
                {
                    ["selector"] = action.Selector,
                    ["key"] = action.Value
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteHover(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("hover 动作必须提供 selector");

            await page.Locator(action.Selector).First.HoverAsync();

            return new BrowserActionResult
            {
                Type = "hover",
                Text = action.Selector,
                Meta =
                {
                    ["selector"] = action.Selector
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteSelectOption(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("select_option 动作必须提供 selector");

            if (string.IsNullOrWhiteSpace(action.Value) && string.IsNullOrWhiteSpace(action.Label))
                throw new Exception("select_option 必须提供 value 或 label");

            if (!string.IsNullOrWhiteSpace(action.Value))
            {
                await page.Locator(action.Selector).First.SelectOptionAsync(new[] { action.Value });
            }
            else
            {
                await page.Locator(action.Selector).First.SelectOptionAsync(new SelectOptionValue
                {
                    Label = action.Label
                });
            }

            return new BrowserActionResult
            {
                Type = "select_option",
                Text = action.Value ?? action.Label ?? "",
                Meta =
                {
                    ["selector"] = action.Selector
                }
            };
        }

        private async Task ClickAndMaybeWait(IPage page, ILocator locator, BrowserAction action, bool force = false)
        {
            if (action.WaitForNavigation)
            {
                var waitState = ParseLoadState(action.WaitUntil);
                await Task.WhenAll(
                    page.WaitForLoadStateAsync(waitState),
                    locator.ClickAsync(new LocatorClickOptions { Force = force })
                );
            }
            else
            {
                await locator.ClickAsync(new LocatorClickOptions { Force = force });
            }
        }

        #endregion

        #region Wait

        private async Task<BrowserActionResult> ExecuteWait(BrowserAction action)
        {
            var ms = (int)((action.Seconds ?? 1) * 1000);
            if (ms < 0) ms = 1000;

            await Task.Delay(ms);

            return new BrowserActionResult
            {
                Type = "wait",
                Text = (action.Seconds ?? 1).ToString()
            };
        }

        private async Task<BrowserActionResult> ExecuteWaitFor(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("wait_for 动作必须提供 selector");

            await page.Locator(action.Selector).First.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = action.TimeoutMs ?? 30000,
                State = ParseWaitForSelectorState(action.WaitForState)
            });

            return new BrowserActionResult
            {
                Type = "wait_for",
                Text = action.Selector
            };
        }

        private async Task<BrowserActionResult> ExecuteWaitForLoadState(IPage page, BrowserAction action)
        {
            var state = ParseLoadState(action.Value ?? action.WaitUntil);
            await page.WaitForLoadStateAsync(state);

            return new BrowserActionResult
            {
                Type = "wait_for_load_state",
                Text = state.ToString()
            };
        }

        private async Task<BrowserActionResult> ExecuteWaitUrlContains(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("wait_url_contains 动作必须提供 value");

            var timeout = action.TimeoutMs ?? 30000;
            var started = DateTime.UtcNow;

            while ((DateTime.UtcNow - started).TotalMilliseconds < timeout)
            {
                if (page.Url.Contains(action.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return new BrowserActionResult
                    {
                        Type = "wait_url_contains",
                        Text = page.Url
                    };
                }

                await Task.Delay(200);
            }

            throw new Exception($"等待 URL 包含 '{action.Value}' 超时");
        }

        #endregion

        #region Scroll

        private async Task<BrowserActionResult> ExecuteScrollBottom(IPage page)
        {
            await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
            return new BrowserActionResult
            {
                Type = "scroll_bottom",
                Text = "ok"
            };
        }

        private async Task<BrowserActionResult> ExecuteScrollUntilText(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("scroll_until_text 动作必须提供 value");

            await ScrollUntilText(page, action.Value, action.MaxScrollCount ?? 10);

            return new BrowserActionResult
            {
                Type = "scroll_until_text",
                Text = action.Value
            };
        }

        #endregion

        #region Read

        private async Task<BrowserActionResult> ExecuteGetText(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("get_text 动作必须提供 selector");

            var text = await page.Locator(action.Selector).First.InnerTextAsync();

            return new BrowserActionResult
            {
                Type = "text",
                Text = text,
                List = new List<string> { text ?? "" },
                Data = new
                {
                    selector = action.Selector,
                    value = text
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteGetTextList(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("get_text_list 动作必须提供 selector");

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();
            var take = action.Take ?? count;

            var list = new List<string>();

            for (int i = 0; i < Math.Min(count, take); i++)
            {
                var text = await loc.Nth(i).InnerTextAsync();
                list.Add((text ?? string.Empty).Trim());
            }

            return new BrowserActionResult
            {
                Type = "text_list",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = list
            };
        }

        private async Task<BrowserActionResult> ExecuteGetAttr(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("get_attr 动作必须提供 selector");

            if (string.IsNullOrWhiteSpace(action.Attr))
                throw new Exception("get_attr 动作必须提供 attr");

            var value = await page.Locator(action.Selector).First.GetAttributeAsync(action.Attr);

            return new BrowserActionResult
            {
                Type = "attr",
                Text = value ?? "",
                List = new List<string> { value ?? "" },
                Data = new
                {
                    selector = action.Selector,
                    attr = action.Attr,
                    value = value ?? ""
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteGetAttrList(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("get_attr_list 动作必须提供 selector");

            var attrName = string.IsNullOrWhiteSpace(action.Attr) ? "title" : action.Attr;

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();
            var take = action.Take ?? count;

            var list = new List<string>();

            for (int i = 0; i < Math.Min(count, take); i++)
            {
                var value = await loc.Nth(i).GetAttributeAsync(attrName);

                if (string.IsNullOrWhiteSpace(value) && action.FallbackToInnerText)
                {
                    value = await loc.Nth(i).InnerTextAsync();
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    list.Add(value.Trim());
                }
            }

            return new BrowserActionResult
            {
                Type = "attr_list",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = list
            };
        }

        private async Task<BrowserActionResult> ExecuteGetLinks(IPage page, BrowserAction action)
        {
            var loc = page.Locator("a");
            var count = await loc.CountAsync();
            var take = action.Take ?? Math.Min(count, 20);

            var data = new List<object>();
            var list = new List<string>();

            for (int i = 0; i < Math.Min(count, take); i++)
            {
                var text = await loc.Nth(i).InnerTextAsync();
                var href = await loc.Nth(i).GetAttributeAsync("href");

                var itemText = $"{(text ?? string.Empty).Trim()} | {href ?? string.Empty}".Trim();
                list.Add(itemText);

                data.Add(new
                {
                    text = (text ?? string.Empty).Trim(),
                    href = href ?? string.Empty
                });
            }

            return new BrowserActionResult
            {
                Type = "links",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = data
            };
        }

        private async Task<BrowserActionResult> ExecuteGetLinksStructured(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("get_links_structured 必须提供 selector");

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();
            var take = action.Take ?? Math.Min(count, 20);

            var rows = new List<object>();
            var list = new List<string>();

            for (int i = 0; i < Math.Min(count, take); i++)
            {
                var el = loc.Nth(i);
                var text = await el.InnerTextAsync();
                var href = await el.GetAttributeAsync("href");

                rows.Add(new
                {
                    title = (text ?? "").Trim(),
                    href = href ?? ""
                });

                list.Add($"{(text ?? "").Trim()} | {href ?? ""}".Trim());
            }

            return new BrowserActionResult
            {
                Type = "links_structured",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = rows
            };
        }

        private async Task<BrowserActionResult> ExecuteGetHtml(IPage page)
        {
            var html = await page.ContentAsync();
            return new BrowserActionResult
            {
                Type = "html",
                Text = html,
                List = new List<string> { html },
                Data = html
            };
        }

        private async Task<BrowserActionResult> ExecuteCheckText(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("check_text 动作必须提供 value");

            var body = await page.Locator("body").InnerTextAsync();
            var exists = body.Contains(action.Value, StringComparison.OrdinalIgnoreCase);

            return new BrowserActionResult
            {
                Type = "check_text",
                Text = exists ? "true" : "false",
                Data = new
                {
                    keyword = action.Value,
                    exists
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteExists(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("exists 动作必须提供 selector");

            var count = await page.Locator(action.Selector).CountAsync();
            var exists = count > 0;

            return new BrowserActionResult
            {
                Type = "exists",
                Text = exists ? "true" : "false",
                Data = new
                {
                    selector = action.Selector,
                    exists,
                    count
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteCount(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("count 动作必须提供 selector");

            var count = await page.Locator(action.Selector).CountAsync();

            return new BrowserActionResult
            {
                Type = "count",
                Text = count.ToString(),
                Count = count,
                Data = new
                {
                    selector = action.Selector,
                    count
                }
            };
        }

        #endregion

        #region Analyze / Extract

        private async Task<BrowserActionResult> AnalyzePage(IPage page)
        {
            var data = await page.EvaluateAsync<object>(@"
() => {
    const buttons = Array.from(document.querySelectorAll('button, input[type=""button""], input[type=""submit""]'))
        .map(x => (x.innerText || x.value || '').trim())
        .filter(x => x)
        .slice(0, 30);

    const inputs = Array.from(document.querySelectorAll('input, textarea, select'))
        .map(x => ({
            tag: x.tagName,
            id: x.id || '',
            name: x.name || '',
            type: x.type || '',
            placeholder: x.placeholder || ''
        }))
        .slice(0, 50);

    const links = Array.from(document.querySelectorAll('a'))
        .map(x => ({
            text: (x.innerText || '').trim(),
            href: x.href || ''
        }))
        .slice(0, 30);

    const frames = Array.from(document.querySelectorAll('iframe, frame'))
        .map(x => ({
            id: x.id || '',
            name: x.name || '',
            src: x.src || ''
        }))
        .slice(0, 20);

    return { buttons, inputs, links, frames };
}");

            return new BrowserActionResult
            {
                Type = "analysis",
                Text = "页面分析完成",
                Data = data
            };
        }

        private async Task<BrowserActionResult> ExtractArticle(IPage page)
        {
            var data = await page.EvaluateAsync<object>(@"
() => {
    const title =
        document.querySelector('h1')?.innerText?.trim() ||
        document.querySelector('.post_title')?.innerText?.trim() ||
        document.title || '';

    const articleRoot =
        document.querySelector('article') ||
        document.querySelector('.post_body') ||
        document.querySelector('.article-content') ||
        document.querySelector('.content') ||
        document.body;

    const paragraphs = Array.from(articleRoot.querySelectorAll('p'))
        .map(x => (x.innerText || '').trim())
        .filter(x => x);

    const content = paragraphs.join('\n');

    return { title, content };
}");

            var title = GetObjectProperty(data, "title");
            var content = GetObjectProperty(data, "content");
            var text = $"{title}\n\n{content}".Trim();

            return new BrowserActionResult
            {
                Type = "article",
                Title = title,
                Text = text,
                List = string.IsNullOrWhiteSpace(content)
                    ? new List<string>()
                    : new List<string> { content },
                Data = data
            };
        }

        private async Task<BrowserActionResult> ExecuteExtractList(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("extract_list 动作必须提供 selector");

            var loc = page.Locator(action.Selector);
            var count = await loc.CountAsync();
            var take = action.Take ?? Math.Min(count, 20);

            var data = new List<object>();
            var list = new List<string>();

            for (int i = 0; i < Math.Min(count, take); i++)
            {
                var item = loc.Nth(i);
                var text = (await item.InnerTextAsync() ?? string.Empty).Trim();
                var href = await item.GetAttributeAsync("href");
                var title = await item.GetAttributeAsync(string.IsNullOrWhiteSpace(action.Attr) ? "title" : action.Attr);

                data.Add(new
                {
                    index = i,
                    text,
                    title = title ?? "",
                    href = href ?? ""
                });

                list.Add($"{text} | {href ?? ""}".Trim());
            }

            return new BrowserActionResult
            {
                Type = "extract_list",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = data
            };
        }

        #endregion

        #region Screenshot / Download

        private async Task<BrowserActionResult> ExecuteScreenshot(IPage page)
        {
            var (path, url, fileName) = EnsureImagePath("screens");

            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                FullPage = true
            });

            return new BrowserActionResult
            {
                Type = "screenshot",
                Text = url,
                Url = url,
                Data = new
                {
                    fileName,
                    path,
                    url
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteScreenshotBase64(IPage page)
        {
            var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true
            });

            var base64 = Convert.ToBase64String(bytes);

            return new BrowserActionResult
            {
                Type = "screenshot_base64",
                Text = base64,
                Data = base64
            };
        }

        private async Task<BrowserActionResult> ExecuteScreenshotElement(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("screenshot_element 动作必须提供 selector");

            var (path, url, fileName) = EnsureImagePath("screens");

            await page.Locator(action.Selector).First.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path
            });

            return new BrowserActionResult
            {
                Type = "element_screenshot",
                Text = url,
                Url = url,
                Data = new
                {
                    selector = action.Selector,
                    fileName,
                    path,
                    url
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteDownload(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("download 动作必须提供 selector");

            var root = _env.WebRootPath;
            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                Directory.CreateDirectory(root);
            }

            var dir = Path.Combine(root, "downloads", DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dir);

            var download = await page.RunAndWaitForDownloadAsync(async () =>
            {
                await page.Locator(action.Selector).First.ClickAsync();
            });

            var fileName = string.IsNullOrWhiteSpace(action.FileName)
                ? download.SuggestedFilename
                : action.FileName;

            var savePath = Path.Combine(dir, fileName);

            await download.SaveAsAsync(savePath);

            return new BrowserActionResult
            {
                Type = "download",
                Text = $"/downloads/{DateTime.Now:yyyyMMdd}/{fileName}",
                Data = new
                {
                    fileName,
                    path = savePath,
                    url = $"/downloads/{DateTime.Now:yyyyMMdd}/{fileName}"
                }
            };
        }

        #endregion

        #region Tabs / Browser State

        private async Task<BrowserActionResult> ExecuteNewTab(BrowserSession session, BrowserAction action)
        {
            var newPage = await session.Context.NewPageAsync();
            session.CurrentPage = newPage;

            if (!string.IsNullOrWhiteSpace(action.Url))
            {
                if (!_browserService.IsAllowedDomain(action.Url))
                    throw new Exception("访问域名不允许");

                await newPage.GotoAsync(action.Url, new PageGotoOptions
                {
                    WaitUntil = ParseWaitUntilState(action.WaitUntil)
                });
            }

            return new BrowserActionResult
            {
                Type = "new_tab",
                Url = newPage.Url,
                Title = await SafeGetTitleAsync(newPage),
                Text = newPage.Url
            };
        }

        private async Task<BrowserActionResult> ExecuteSwitchTab(BrowserSession session, BrowserAction action)
        {
            var index = action.Index ?? 0;
            if (action.Index == null && !string.IsNullOrWhiteSpace(action.Value))
            {
                if (!int.TryParse(action.Value, out index))
                    throw new Exception("switch_tab 的 value 必须是数字");
            }

            if (index < 0 || index >= session.Context.Pages.Count)
                throw new Exception("tab 索引超出范围");

            session.CurrentPage = session.Context.Pages[index];

            return new BrowserActionResult
            {
                Type = "switch_tab",
                Url = session.CurrentPage.Url,
                Title = await SafeGetTitleAsync(session.CurrentPage),
                Text = session.CurrentPage.Url,
                Meta =
                {
                    ["index"] = index
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteGetTabs(BrowserSession session)
        {
            var tabs = new List<object>();
            var list = new List<string>();

            for (int i = 0; i < session.Context.Pages.Count; i++)
            {
                var p = session.Context.Pages[i];
                var title = await SafeGetTitleAsync(p);

                tabs.Add(new
                {
                    index = i,
                    url = p.Url,
                    title
                });

                list.Add($"{i} | {title} | {p.Url}");
            }

            return new BrowserActionResult
            {
                Type = "tabs",
                Text = string.Join("\n", list),
                List = list,
                Count = list.Count,
                Data = tabs
            };
        }

        private async Task<BrowserActionResult> ExecuteClearCookies(BrowserSession session)
        {
            await session.Context.ClearCookiesAsync();

            return new BrowserActionResult
            {
                Type = "clear_cookies",
                Text = "ok"
            };
        }

        #endregion

        #region Login / Captcha / JS

        private async Task<BrowserActionResult> ExecuteAutoDetectLogin(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Username) || string.IsNullOrWhiteSpace(action.Password))
                throw new Exception("username/password required");

            var data = await page.EvaluateAsync<object>(@"
(args) => {
    const username = args.username;
    const password = args.password;

    const pwd = document.querySelector('input[type=password]');
    if(!pwd) return {success:false, reason:'password not found'};

    const form = pwd.closest('form') || document;
    const user = form.querySelector('input[type=text],input[type=email]');

    if(user){
        user.value = username;
        user.dispatchEvent(new Event('input',{bubbles:true}));
        user.dispatchEvent(new Event('change',{bubbles:true}));
    }

    pwd.value = password;
    pwd.dispatchEvent(new Event('input',{bubbles:true}));
    pwd.dispatchEvent(new Event('change',{bubbles:true}));

    const btn = form.querySelector('button, input[type=submit]');
    if(btn){
        btn.click();
    }

    return {success:true};
}", new
            {
                username = action.Username,
                password = action.Password
            });

            return new BrowserActionResult
            {
                Type = "auto_login",
                Text = "ok",
                Data = data
            };
        }

        private async Task<BrowserActionResult> ExecuteAnalyzeLoginForm(IPage page)
        {
            var data = await page.EvaluateAsync<object>(@"
() => {
    const allInputs = Array.from(document.querySelectorAll('input'));

    const normalizeSelector = (el) => {
        if (!el) return null;
        if (el.id) return '#' + el.id;
        if (el.name) return `input[name=""${el.name}""]`;
        return null;
    };

    const username = allInputs.find(x => /user|account|name|login/i.test(
        `${x.id} ${x.name} ${x.placeholder} ${x.type}`) && (x.type === 'text' || x.type === 'email' || x.type === ''));

    const password = allInputs.find(x => /pass|pwd/i.test(
        `${x.id} ${x.name} ${x.placeholder} ${x.type}`) || x.type === 'password');

    const captchaInput = allInputs.find(x => /captcha|verify|code|check|验证码|校验码/i.test(
        `${x.id} ${x.name} ${x.placeholder}`));

    const buttons = Array.from(document.querySelectorAll('button, input[type=""button""], input[type=""submit""]'));
    const loginButton = buttons.find(x => /登录|login|sign in/i.test((x.innerText || x.value || '').trim()));

    const imgs = Array.from(document.querySelectorAll('img'));
    const captchaImage = imgs.find(x => /captcha|verify|code|check/i.test(
        `${x.id} ${x.className} ${x.src} ${x.alt}`));

    return {
        usernameSelector: normalizeSelector(username),
        passwordSelector: normalizeSelector(password),
        captchaInputSelector: normalizeSelector(captchaInput),
        captchaImageSelector: captchaImage ? (captchaImage.id ? '#' + captchaImage.id : null) : null,
        loginButtonText: loginButton ? ((loginButton.innerText || loginButton.value || '').trim()) : null
    };
}");

            return new BrowserActionResult
            {
                Type = "auto_login_form",
                Text = "登录表单分析完成",
                Data = data
            };
        }

        private async Task<BrowserActionResult> ExecuteEvaluate(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Script) && string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("evaluate 动作必须提供 script 或 value");

            var script = action.Script ?? action.Value!;
            var result = await page.EvaluateAsync<object>(script);

            return new BrowserActionResult
            {
                Type = "evaluate",
                Text = result?.ToString() ?? "",
                Data = result
            };
        }

        private async Task<BrowserActionResult> ExecuteDetectCaptcha(IPage page)
        {
            var result = await page.EvaluateAsync<object>(@"
() => {
    const imgs = Array.from(document.querySelectorAll('img')).map(x => ({
        tag: 'img',
        id: x.id || '',
        className: x.className || '',
        src: x.src || '',
        alt: x.alt || ''
    }));

    const inputs = Array.from(document.querySelectorAll('input')).map(x => ({
        tag: 'input',
        id: x.id || '',
        name: x.name || '',
        type: x.type || '',
        placeholder: x.placeholder || ''
    }));

    const bodyText = document.body ? (document.body.innerText || '') : '';

    const imgCandidate = imgs.find(x => /captcha|verify|code|check/i.test(
        `${x.id} ${x.className} ${x.src} ${x.alt}`));

    const inputCandidate = inputs.find(x => /captcha|verify|code|check/i.test(
        `${x.id} ${x.name} ${x.placeholder}`));

    const hasCaptchaText = /验证码|校验码|安全验证|verification code|captcha/i.test(bodyText);

    return {
        hasCaptcha: !!imgCandidate || !!inputCandidate || hasCaptchaText,
        imageCandidate: imgCandidate || null,
        inputCandidate: inputCandidate || null,
        hasCaptchaText
    };
}");

            return new BrowserActionResult
            {
                Type = "detect_captcha",
                Text = "验证码检测完成",
                Data = result
            };
        }

        private async Task<BrowserActionResult> ExecuteCaptureCaptcha(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("capture_captcha 动作必须提供 selector");

            var (path, url, fileName) = EnsureImagePath("captcha");

            await page.Locator(action.Selector).First.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path
            });

            return new BrowserActionResult
            {
                Type = "captcha_image",
                Text = url,
                Url = url,
                Data = new
                {
                    selector = action.Selector,
                    fileName,
                    path,
                    url,
                    message = "请人工查看验证码图片并调用 submit_captcha 填写验证码"
                }
            };
        }

        private async Task<BrowserActionResult> ExecuteSubmitCaptcha(IPage page, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Selector))
                throw new Exception("submit_captcha 动作必须提供验证码输入框 selector");

            if (string.IsNullOrWhiteSpace(action.Value))
                throw new Exception("submit_captcha 动作必须提供验证码 value");

            await page.Locator(action.Selector).First.FillAsync(action.Value);

            return new BrowserActionResult
            {
                Type = "submit_captcha",
                Text = action.Value,
                Meta =
                {
                    ["selector"] = action.Selector
                }
            };
        }

        #endregion

        #region Helpers

        private async Task ScrollUntilText(IPage page, string text, int maxScrollCount)
        {
            for (int i = 0; i < maxScrollCount; i++)
            {
                var body = await page.Locator("body").InnerTextAsync();

                if (body.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return;

                await page.EvaluateAsync("() => window.scrollBy(0, 1000)");
                await Task.Delay(800);
            }
        }

        private static LoadState ParseLoadState(string? state)
        {
            return (state ?? "").Trim().ToLowerInvariant() switch
            {
                "load" => LoadState.Load,
                "networkidle" => LoadState.NetworkIdle,
                "domcontentloaded" => LoadState.DOMContentLoaded,
                _ => LoadState.DOMContentLoaded
            };
        }

        private static WaitUntilState ParseWaitUntilState(string? state)
        {
            return (state ?? "").Trim().ToLowerInvariant() switch
            {
                "load" => WaitUntilState.Load,
                "networkidle" => WaitUntilState.NetworkIdle,
                "domcontentloaded" => WaitUntilState.DOMContentLoaded,
                "commit" => WaitUntilState.Commit,
                _ => WaitUntilState.DOMContentLoaded
            };
        }

        private static WaitForSelectorState ParseWaitForSelectorState(string? state)
        {
            return (state ?? "").Trim().ToLowerInvariant() switch
            {
                "attached" => WaitForSelectorState.Attached,
                "detached" => WaitForSelectorState.Detached,
                "hidden" => WaitForSelectorState.Hidden,
                "visible" => WaitForSelectorState.Visible,
                _ => WaitForSelectorState.Visible
            };
        }

        private static async Task<string> SafeGetTitleAsync(IPage page)
        {
            try
            {
                return await page.TitleAsync();
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ToAbsoluteUrl(string baseUrl, string raw)
        {
            if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (Uri.TryCreate(new Uri(baseUrl), raw, out var combined))
                return combined.ToString();

            return raw;
        }

        private (string path, string url, string fileName) EnsureImagePath(string folderName)
        {
            var root = _env.WebRootPath;

            if (string.IsNullOrEmpty(root))
            {
                root = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                Directory.CreateDirectory(root);
            }

            var dateFolder = DateTime.Now.ToString("yyyyMMdd");
            var dir = Path.Combine(root, folderName, dateFolder);
            Directory.CreateDirectory(dir);

            var fileName = $"{Guid.NewGuid():N}.png";
            var path = Path.Combine(dir, fileName);
            var url = $"/{folderName}/{dateFolder}/{fileName}";

            return (path, url, fileName);
        }

        private string? GetObjectProperty(object? obj, string propertyName)
        {
            if (obj == null) return null;
            return obj.GetType().GetProperty(propertyName)?.GetValue(obj)?.ToString();
        }

        private BrowserFinalResult BuildFinalResult(List<BrowserActionResult> outputs)
        {
            if (outputs == null || outputs.Count == 0)
            {
                return new BrowserFinalResult();
            }

            var last = outputs.Last();

            var dataJson = last.Data == null
                ? "[]"
                : System.Text.Json.JsonSerializer.Serialize(last.Data);

            return new BrowserFinalResult
            {
                FinalType = last.Type ?? "",
                FinalText = last.Text ?? "",
                FinalDataJson = dataJson,
                FinalList = last.List ?? new List<string>(),
                FinalData = last.Data
            };
        }

        #endregion
    }
}
