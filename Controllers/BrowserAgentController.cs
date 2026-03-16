using AiApi.Models;
using AiApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace AiApi.Controllers
{
    /// <summary>
    /// AI 浏览器控制器（生产级版本）
    ///
    /// 设计目标：
    /// 1. 让 AI / Coze / Swagger 可以通过 JSON actions 驱动浏览器
    /// 2. 保持接口稳定、返回结构统一、便于调试
    /// 3. 尽量接近“ChatGPT Browser”风格：支持页面分析、结构化抓取、文章提取、验证码检测等
    ///
    /// 说明：
    /// - 本控制器不做“自动绕过验证码”，而是提供“检测验证码 / 截图验证码 / 提交验证码”的人工协作方案
    /// - 这更适合部署在 Linux 服务器上的 WebAPI，也更容易长期稳定运行
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

        /// <summary>
        /// 手动创建一个浏览器 Session
        /// 通常不一定需要先调用本接口，因为 /run 会自动创建 Session
        /// </summary>
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
        /// 执行浏览器动作序列
        ///
        /// 使用方式：
        /// POST /AiApi/Browser/run
        ///
        /// 示例：
        /// {
        ///   "actions": [
        ///     { "type": "goto", "url": "https://news.163.com/" },
        ///     { "type": "wait", "seconds": 2 },
        ///     { "type": "analyze_page" }
        ///   ]
        /// }
        ///
        /// 说明：
        /// - 如果未传 SessionId，会自动创建
        /// - 如果传了 SessionId 但不存在，也会自动创建
        /// - 如果 CloseSession=true，则执行完成后自动关闭 Session
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
                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    session = _browserService.GetSession(request.SessionId);
                }

                if (session == null)
                {
                    session = await _browserService.CreateSessionAsync();
                }

                var outputs = new List<object>();
                var logs = new List<object>();

                foreach (var action in request.Actions)
                {
                    var startTime = DateTime.UtcNow;

                    try
                    {
                        var result = await ExecuteAction(session, action);

                        if (result != null)
                        {
                            outputs.Add(result);
                        }

                        logs.Add(new
                        {
                            action = action.Type,
                            durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "BrowserAction 执行失败: {ActionType}", action.Type);

                        if (string.Equals(action.OnError, "skip", StringComparison.OrdinalIgnoreCase))
                        {
                            outputs.Add(new
                            {
                                type = "error",
                                action = action.Type,
                                message = ex.Message
                            });

                            logs.Add(new
                            {
                                action = action.Type,
                                durationMs = (DateTime.UtcNow - startTime).TotalMilliseconds,
                                error = ex.Message
                            });

                            continue;
                        }

                        return Ok(new
                        {
                            success = false,
                            sessionId = session.SessionId,
                            page = new
                            {
                                url = session.CurrentPage.Url,
                                title = await SafeGetTitleAsync(session.CurrentPage)
                            },
                            error = ex.Message,
                            errorDetail = ex.ToString(),
                            outputs,
                            logs
                        });
                    }
                }

                var response = new
                {
                    success = true,
                    sessionId = session.SessionId,
                    page = new
                    {
                        url = session.CurrentPage.Url,
                        title = await SafeGetTitleAsync(session.CurrentPage)
                    },
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

        /// <summary>
        /// 关闭指定 Session
        /// </summary>
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

        /// <summary>
        /// 查看当前所有 Session
        /// </summary>
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

        /// <summary>
        /// 执行浏览器自动化动作（核心引擎）
        ///
        /// 支持的 action.Type：
        ///
        /// 页面导航
        /// - goto                 打开网页 URL
        /// - goto_attr            从元素读取 href 并打开
        /// - back                 后退
        /// - forward              前进
        /// - refresh              刷新页面
        ///
        /// 页面交互
        /// - click                点击 selector 匹配的第一个元素
        /// - click_index          点击 selector 的第 N 个元素（value 传索引）
        /// - click_text           按文字点击
        /// - fill                 输入文本
        /// - press                键盘按键
        /// - hover                鼠标悬停
        ///
        /// 等待相关
        /// - wait                 等待若干秒
        /// - wait_for             等待元素出现
        /// - wait_for_load_state  等待页面加载状态
        /// - wait_url_contains    等待 URL 包含指定文本
        ///
        /// 页面滚动
        /// - scroll_bottom        滚动到底部
        /// - scroll_until_text    滚动直到出现某段文字
        ///
        /// 数据读取
        /// - get_text             获取元素文本
        /// - get_text_list        获取多个元素文本
        /// - get_attr             获取属性
        /// - get_attr_list        获取多个元素属性
        /// - get_links            抓取所有链接
        /// - get_links_structured 抓取结构化链接 {title, href}
        /// - get_html             获取 HTML
        /// - check_text           检查页面是否包含某段文字
        ///
        /// 页面分析 / 内容提取
        /// - analyze_page         分析页面结构（按钮、输入框、链接、iframe）
        /// - extract_article      尝试提取文章标题 + 正文
        /// - extract_list         提取 selector 下的结构化列表
        ///
        /// 标签页 / 浏览器状态
        /// - new_tab              新建标签页
        /// - switch_tab           切换标签页
        /// - get_tabs             获取所有标签页
        /// - clear_cookies        清除 cookies
        ///
        /// 文件 / 图像
        /// - screenshot           页面截图并保存
        /// - screenshot_base64    返回 base64 截图
        /// - screenshot_element   元素截图
        /// - download             下载文件
        ///
        /// JavaScript
        /// - evaluate             执行脚本
        ///
        /// 验证码相关（生产可用方案）
        /// - detect_captcha       检测页面是否疑似存在验证码
        /// - capture_captcha      截图验证码元素，返回图片地址
        /// - submit_captcha       向验证码输入框填写人工输入的验证码
        /// - auto_login_form      分析登录表单结构（用户名/密码/验证码/按钮）
        ///
        /// 说明：
        /// - 本方法是整个 Browser Agent 的核心
        /// - AI 只需传 JSON actions，本方法负责执行
        /// - 所有复杂任务都可以拆成多个 actions 顺序执行
        /// </summary>
        private async Task<object?> ExecuteAction(BrowserSession session, BrowserAction action)
        {
            if (string.IsNullOrWhiteSpace(action.Type))
                throw new Exception("action.type 不能为空");

            var page = session.CurrentPage;
            var actionType = action.Type.Trim().ToLowerInvariant();

            switch (actionType)
            {
                #region 页面导航

                case "goto":
                    {
                        if (string.IsNullOrWhiteSpace(action.Url))
                            throw new Exception("goto 动作必须提供 url");

                        if (!_browserService.IsAllowedDomain(action.Url))
                            throw new Exception("访问域名不允许");

                        await page.GotoAsync(action.Url, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded
                        });

                        return new
                        {
                            type = "goto",
                            url = page.Url,
                            title = await SafeGetTitleAsync(page)
                        };
                    }

                case "goto_attr":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("goto_attr 动作必须提供 selector");

                        var attr = string.IsNullOrWhiteSpace(action.Attr) ? "href" : action.Attr;

                        var value = await page.Locator(action.Selector).First.GetAttributeAsync(attr);

                        if (string.IsNullOrWhiteSpace(value))
                            throw new Exception($"未找到属性 {attr}");

                        if (!_browserService.IsAllowedDomain(value))
                            throw new Exception("访问域名不允许");

                        await page.GotoAsync(value, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded
                        });

                        return new
                        {
                            type = "goto_attr",
                            selector = action.Selector,
                            attr,
                            url = value
                        };
                    }

                case "back":
                    {
                        await page.GoBackAsync();
                        return new
                        {
                            type = "back",
                            url = page.Url,
                            title = await SafeGetTitleAsync(page)
                        };
                    }

                case "forward":
                    {
                        await page.GoForwardAsync();
                        return new
                        {
                            type = "forward",
                            url = page.Url,
                            title = await SafeGetTitleAsync(page)
                        };
                    }

                case "refresh":
                    {
                        await page.ReloadAsync();
                        return new
                        {
                            type = "refresh",
                            url = page.Url,
                            title = await SafeGetTitleAsync(page)
                        };
                    }

                #endregion

                #region 页面交互

                case "click":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("click 动作必须提供 selector");

                        await page.Locator(action.Selector).First.ClickAsync();

                        return new
                        {
                            type = "click",
                            selector = action.Selector
                        };
                    }

                case "click_index":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("click_index 动作必须提供 selector");

                        var index = 0;
                        if (!string.IsNullOrWhiteSpace(action.Value))
                            int.TryParse(action.Value, out index);

                        var loc = page.Locator(action.Selector);
                        var count = await loc.CountAsync();

                        if (index < 0 || index >= count)
                            throw new Exception($"index 超出范围，当前元素数量: {count}");

                        await loc.Nth(index).ClickAsync(new LocatorClickOptions
                        {
                            Force = true
                        });

                        return new
                        {
                            type = "click_index",
                            selector = action.Selector,
                            index
                        };
                    }

                case "click_text":
                    {
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("click_text 动作必须提供 value");

                        await page.GetByText(action.Value, new PageGetByTextOptions
                        {
                            Exact = false
                        }).First.ClickAsync();

                        return new
                        {
                            type = "click_text",
                            text = action.Value
                        };
                    }

                case "fill":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("fill 动作必须提供 selector");

                        await page.Locator(action.Selector).First.FillAsync(action.Value ?? string.Empty);

                        return new
                        {
                            type = "fill",
                            selector = action.Selector,
                            value = action.Value
                        };
                    }

                case "press":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("press 动作必须提供 selector");

                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("press 动作必须提供 value");

                        await page.Locator(action.Selector).First.PressAsync(action.Value);

                        return new
                        {
                            type = "press",
                            selector = action.Selector,
                            key = action.Value
                        };
                    }

                case "hover":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("hover 动作必须提供 selector");

                        await page.Locator(action.Selector).First.HoverAsync();

                        return new
                        {
                            type = "hover",
                            selector = action.Selector
                        };
                    }

                #endregion

                #region 等待相关

                case "wait":
                    {
                        var ms = (int)((action.Seconds ?? 1) * 1000);
                        if (ms < 0) ms = 1000;

                        await Task.Delay(ms);

                        return new
                        {
                            type = "wait",
                            seconds = action.Seconds ?? 1
                        };
                    }

                case "wait_for":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("wait_for 动作必须提供 selector");

                        await page.Locator(action.Selector).First.WaitForAsync(new LocatorWaitForOptions
                        {
                            Timeout = action.TimeoutMs ?? 30000
                        });

                        return new
                        {
                            type = "wait_for",
                            selector = action.Selector
                        };
                    }

                case "wait_for_load_state":
                    {
                        var state = ParseLoadState(action.Value);
                        await page.WaitForLoadStateAsync(state);

                        return new
                        {
                            type = "wait_for_load_state",
                            state = state.ToString()
                        };
                    }

                case "wait_url_contains":
                    {
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("wait_url_contains 动作必须提供 value");

                        var timeout = action.TimeoutMs ?? 30000;
                        var started = DateTime.UtcNow;

                        while ((DateTime.UtcNow - started).TotalMilliseconds < timeout)
                        {
                            if (page.Url.Contains(action.Value, StringComparison.OrdinalIgnoreCase))
                            {
                                return new
                                {
                                    type = "wait_url_contains",
                                    keyword = action.Value,
                                    url = page.Url
                                };
                            }

                            await Task.Delay(200);
                        }

                        throw new Exception($"等待 URL 包含 '{action.Value}' 超时");
                    }

                #endregion

                #region 页面滚动

                case "scroll_bottom":
                    {
                        await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");

                        return new
                        {
                            type = "scroll_bottom"
                        };
                    }

                case "scroll_until_text":
                    {
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("scroll_until_text 动作必须提供 value");

                        await ScrollUntilText(page, action.Value, action.MaxScrollCount ?? 10);

                        return new
                        {
                            type = "scroll_until_text",
                            keyword = action.Value
                        };
                    }

                #endregion

                #region 数据读取

                case "get_text":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("get_text 动作必须提供 selector");

                        var text = await page.Locator(action.Selector).First.InnerTextAsync();

                        return new
                        {
                            type = "text",
                            selector = action.Selector,
                            value = text
                        };
                    }

                case "get_text_list":
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

                        return new
                        {
                            type = "text_list",
                            selector = action.Selector,
                            count = list.Count,
                            data = list
                        };
                    }

                case "get_attr":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("get_attr 动作必须提供 selector");

                        if (string.IsNullOrWhiteSpace(action.Attr))
                            throw new Exception("get_attr 动作必须提供 attr");

                        var value = await page.Locator(action.Selector).First.GetAttributeAsync(action.Attr);

                        return new
                        {
                            type = "attr",
                            selector = action.Selector,
                            attr = action.Attr,
                            value
                        };
                    }

                case "get_attr_list":
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

                        return new
                        {
                            type = "attr_list",
                            selector = action.Selector,
                            attr = attrName,
                            count = list.Count,
                            data = list
                        };
                    }

                case "get_links":
                    {
                        var loc = page.Locator("a");
                        var count = await loc.CountAsync();
                        var take = action.Take ?? Math.Min(count, 20);

                        var data = new List<object>();

                        for (int i = 0; i < Math.Min(count, take); i++)
                        {
                            var text = await loc.Nth(i).InnerTextAsync();
                            var href = await loc.Nth(i).GetAttributeAsync("href");

                            data.Add(new
                            {
                                text = (text ?? string.Empty).Trim(),
                                href = href ?? string.Empty
                            });
                        }

                        return new
                        {
                            type = "links",
                            count = data.Count,
                            data
                        };
                    }

                case "get_links_structured":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("get_links_structured 必须提供 selector");

                        var loc = page.Locator(action.Selector);
                        var count = await loc.CountAsync();
                        var take = action.Take ?? Math.Min(count, 20);

                        var list = new List<object>();

                        for (int i = 0; i < Math.Min(count, take); i++)
                        {
                            var el = loc.Nth(i);
                            var text = await el.InnerTextAsync();
                            var href = await el.GetAttributeAsync("href");

                            list.Add(new
                            {
                                title = (text ?? "").Trim(),
                                href = href ?? ""
                            });
                        }

                        return new
                        {
                            type = "links_structured",
                            selector = action.Selector,
                            count = list.Count,
                            data = list
                        };
                    }

                case "get_html":
                    {
                        var html = await page.ContentAsync();

                        return new
                        {
                            type = "html",
                            html
                        };
                    }

                case "check_text":
                    {
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("check_text 动作必须提供 value");

                        var body = await page.Locator("body").InnerTextAsync();
                        var exists = body.Contains(action.Value, StringComparison.OrdinalIgnoreCase);

                        return new
                        {
                            type = "check_text",
                            keyword = action.Value,
                            exists
                        };
                    }

                #endregion

                #region 页面分析 / 内容提取

                case "analyze_page":
                    {
                        return await AnalyzePage(page);
                    }

                case "extract_article":
                    {
                        return await ExtractArticle(page);
                    }

                case "extract_list":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("extract_list 动作必须提供 selector");

                        var loc = page.Locator(action.Selector);
                        var count = await loc.CountAsync();
                        var take = action.Take ?? Math.Min(count, 20);

                        var data = new List<object>();

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
                        }

                        return new
                        {
                            type = "extract_list",
                            selector = action.Selector,
                            count = data.Count,
                            data
                        };
                    }

                #endregion

                #region 文件 / 图像

                case "screenshot":
                    {
                        var (path, url, fileName) = EnsureImagePath("screens");

                        await page.ScreenshotAsync(new PageScreenshotOptions
                        {
                            Path = path,
                            FullPage = true
                        });

                        _logger.LogInformation("页面截图保存成功: {Path}", path);

                        return new
                        {
                            type = "screenshot",
                            fileName,
                            path,
                            url
                        };
                    }

                case "screenshot_base64":
                    {
                        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
                        {
                            FullPage = true
                        });

                        return new
                        {
                            type = "screenshot_base64",
                            data = Convert.ToBase64String(bytes)
                        };
                    }

                case "screenshot_element":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("screenshot_element 动作必须提供 selector");

                        var (path, url, fileName) = EnsureImagePath("screens");

                        await page.Locator(action.Selector).First.ScreenshotAsync(new LocatorScreenshotOptions
                        {
                            Path = path
                        });

                        _logger.LogInformation("元素截图保存成功: {Path}", path);

                        return new
                        {
                            type = "element_screenshot",
                            selector = action.Selector,
                            fileName,
                            path,
                            url
                        };
                    }

                case "download":
                    {
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("download 动作必须提供 selector");

                        var dir = Path.Combine(_env.WebRootPath, "downloads", DateTime.Now.ToString("yyyyMMdd"));
                        Directory.CreateDirectory(dir);

                        var download = await page.RunAndWaitForDownloadAsync(async () =>
                        {
                            await page.Locator(action.Selector).First.ClickAsync();
                        });

                        var fileName = download.SuggestedFilename;
                        var savePath = Path.Combine(dir, fileName);
                        await download.SaveAsAsync(savePath);

                        return new
                        {
                            type = "download",
                            fileName,
                            path = savePath,
                            url = $"/downloads/{DateTime.Now:yyyyMMdd}/{fileName}"
                        };
                    }

                #endregion

                #region 浏览器 / 标签页

                case "new_tab":
                    {
                        var newPage = await session.Context.NewPageAsync();
                        session.CurrentPage = newPage;

                        if (!string.IsNullOrWhiteSpace(action.Url))
                        {
                            if (!_browserService.IsAllowedDomain(action.Url))
                                throw new Exception("访问域名不允许");

                            await newPage.GotoAsync(action.Url, new PageGotoOptions
                            {
                                WaitUntil = WaitUntilState.DOMContentLoaded
                            });
                        }

                        return new
                        {
                            type = "new_tab",
                            url = newPage.Url,
                            title = await SafeGetTitleAsync(newPage)
                        };
                    }

                case "switch_tab":
                    {
                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("switch_tab 动作必须提供 value，值为 tab 索引");

                        if (!int.TryParse(action.Value, out var index))
                            throw new Exception("switch_tab 的 value 必须是数字");

                        if (index < 0 || index >= session.Context.Pages.Count)
                            throw new Exception("tab 索引超出范围");

                        session.CurrentPage = session.Context.Pages[index];

                        return new
                        {
                            type = "switch_tab",
                            index,
                            url = session.CurrentPage.Url,
                            title = await SafeGetTitleAsync(session.CurrentPage)
                        };
                    }

                case "get_tabs":
                    {
                        var tabs = new List<object>();

                        for (int i = 0; i < session.Context.Pages.Count; i++)
                        {
                            var p = session.Context.Pages[i];
                            tabs.Add(new
                            {
                                index = i,
                                url = p.Url,
                                title = await SafeGetTitleAsync(p)
                            });
                        }

                        return new
                        {
                            type = "tabs",
                            count = tabs.Count,
                            data = tabs
                        };
                    }

                case "clear_cookies":
                    {
                        await session.Context.ClearCookiesAsync();

                        return new
                        {
                            type = "clear_cookies"
                        };
                    }

                #endregion

                #region JavaScript

                case "evaluate":
                    {
                        if (string.IsNullOrWhiteSpace(action.Script) && string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("evaluate 动作必须提供 script 或 value");

                        var script = action.Script ?? action.Value!;
                        var result = await page.EvaluateAsync<object>(script);

                        return new
                        {
                            type = "evaluate",
                            result
                        };
                    }

                #endregion

                #region 验证码 / 登录分析

                case "detect_captcha":
                    {
                        // 通过页面中的 img / input / 文本特征做启发式检测
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

                        return new
                        {
                            type = "detect_captcha",
                            data = result
                        };
                    }

                case "capture_captcha":
                    {
                        // 生产推荐方案：人工协作
                        // 先截图验证码元素，再由前端/用户输入验证码，随后再调用 submit_captcha
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("capture_captcha 动作必须提供 selector");

                        var (path, url, fileName) = EnsureImagePath("captcha");

                        await page.Locator(action.Selector).First.ScreenshotAsync(new LocatorScreenshotOptions
                        {
                            Path = path
                        });

                        _logger.LogInformation("验证码截图保存成功: {Path}", path);

                        return new
                        {
                            type = "captcha_image",
                            selector = action.Selector,
                            fileName,
                            path,
                            url,
                            message = "请人工查看验证码图片并调用 submit_captcha 填写验证码"
                        };
                    }

                case "submit_captcha":
                    {
                        // selector = 验证码输入框
                        // value    = 人工输入的验证码内容
                        if (string.IsNullOrWhiteSpace(action.Selector))
                            throw new Exception("submit_captcha 动作必须提供验证码输入框 selector");

                        if (string.IsNullOrWhiteSpace(action.Value))
                            throw new Exception("submit_captcha 动作必须提供验证码 value");

                        await page.Locator(action.Selector).First.FillAsync(action.Value);

                        return new
                        {
                            type = "submit_captcha",
                            selector = action.Selector,
                            value = action.Value
                        };
                    }

                case "auto_login_form":
                    {
                        // 尝试自动分析登录表单结构
                        // 让 AI 不需要一开始就完全知道 selector
                        var result = await page.EvaluateAsync<object>(@"
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
    const loginButton = buttons.find(x => /登录|登 录|login|sign in/i.test((x.innerText || x.value || '').trim()));

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

                        return new
                        {
                            type = "auto_login_form",
                            data = result
                        };
                    }

                #endregion

                default:
                    throw new Exception($"不支持的动作类型: {action.Type}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 自动滚动页面，直到页面正文出现某段文字
        /// 常用于无限滚动页面 / 延迟加载页面
        /// </summary>
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

        /// <summary>
        /// 分析页面结构，供 AI / Swagger 调试使用
        ///
        /// 返回：
        /// - buttons  页面上的按钮文本
        /// - inputs   输入框信息
        /// - links    部分链接
        /// - frames   iframe / frame 信息
        /// </summary>
        private async Task<object> AnalyzePage(IPage page)
        {
            var result = await page.EvaluateAsync<object>(@"
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

            return new
            {
                type = "analysis",
                data = result
            };
        }

        /// <summary>
        /// 尝试提取文章页的“标题 + 正文”
        /// 适合新闻、博客、公告页的快速提取
        /// </summary>
        private async Task<object> ExtractArticle(IPage page)
        {
            var result = await page.EvaluateAsync<object>(@"
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

            return new
            {
                type = "article",
                data = result
            };
        }

        /// <summary>
        /// 解析 Playwright 的加载状态
        /// 支持：
        /// load / networkidle / domcontentloaded
        /// </summary>
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

        /// <summary>
        /// 安全获取标题，避免页面异常时抛出错误
        /// </summary>
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

        /// <summary>
        /// 为截图类动作生成标准图片路径
        ///
        /// 目录格式：
        /// wwwroot/{folderName}/yyyyMMdd/{guid}.png
        ///
        /// 返回：
        /// - path     磁盘路径
        /// - url      可访问 URL
        /// - fileName 文件名
        /// </summary>
        private (string path, string url, string fileName) EnsureImagePath(string folderName)
        {
            var root = _env.WebRootPath;

            // 如果没有 wwwroot 自动创建
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


        #endregion
    }
}
