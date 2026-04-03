using AiApi.Models;
using AiApi.Services;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Data.OleDb;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TangYuan.Models;
using TangYuan.Tools;
using WebApi.Tools;

namespace TangYuan.Controllers
{
    /// <summary>
    /// 技能控制器
    ///
    /// 设计目标：
    /// 1. 支持原子技能独立执行（文件、打印、截图、邮件、浏览器等）
    /// 2. 支持数据库中定义的组合技能（工作流）
    /// 3. 支持步骤之间通过模板变量传值，例如：
    ///    {{step0}}
    ///    {{step0.path}}
    ///    {{step0.data.path}}
    /// 4. 返回结果尽量稳定，方便 AI 调用
    /// </summary>
    [Authorize(AuthenticationSchemes = "ApiKey")]
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : BaseCommandController
    {
        private readonly ILogger<SkillsController> _logger;
        private readonly BrowserService _browserService;
        private readonly FileSystemOptions _fsOptions;

        /// <summary>
        /// 允许执行的外部 exe 白名单
        /// 可执行文件需要在配置文件中配置
        /// </summary>
        private static readonly HashSet<string> AllowedExeNames = LoadAllowedExeNames();

        private static HashSet<string> LoadAllowedExeNames()
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();

            var exeNames = configuration.GetSection("AllowedExeNames").Get<List<string>>()
                           ?? new List<string>();

            return new HashSet<string>(exeNames, StringComparer.OrdinalIgnoreCase);
        }

        public SkillsController(
            IConfiguration configuration,
            ILogger<SkillsController> logger,
            BrowserService browserService,
            IOptions<FileSystemOptions> fsOptions)
            : base(configuration, logger)
        {
            _logger = logger;
            _browserService = browserService;
            _fsOptions = fsOptions.Value;
        }

        #region AI技能相关

        /*
            1. 先调用 GetSkillListForAI 查看有哪些 workflow 和 builtin skill。
            2. 如果 workflows 中已有可直接完成任务的技能，优先调用 GetSkillAction 查看详情，再调用 ExecuteSkill。
            3. 如果没有合适 workflow，再调用 GetBuiltinSkillManifest 查看 builtin skill 的调用方式。
            4. 所有真正执行动作的请求，统一调用 ExecuteSkill。
         */

        /// <summary>
        /// 给 AI 返回技能总览：
        /// 1. workflows = 数据库中的组合技能（优先直接调用）
        /// 2. builtins  = skill-manifest.json 中定义的内置原子技能（没有现成技能时再组合使用）
        ///
        /// AI 推荐使用策略：
        /// - 先看 workflows 有没有现成技能
        /// - 如果没有，再看 builtins 并调用 GetBuiltinSkillManifest
        /// </summary>
        [HttpPost("GetSkillListForAI")]
        public async Task<IActionResult> GetSkillListForAI()
        {
            try
            {
                // 1. 数据库中的组合技能（给 AI 直接调用）
                var sql = "SELECT SkillCode, Remark AS AIDesc FROM Skills ORDER BY ID ASC";
                var workflows = (await QueryAsync<dynamic>(sql)).ToList();

                // 2. 从 manifest 文件中读取内置原子技能
                var builtins = new List<object>();
                var filePath = Path.Combine(AppContext.BaseDirectory, "Config", "skill-manifest.json");

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("未找到内置技能 manifest 文件：{FilePath}", filePath);
                }
                else
                {
                    var json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);

                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("skills", out var skillsElement) &&
                        skillsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in skillsElement.EnumerateArray())
                        {
                            string skillCode = "";
                            string aiDesc = "";

                            if (item.TryGetProperty("skillCode", out var codeElement))
                                skillCode = codeElement.GetString() ?? "";

                            if (item.TryGetProperty("desc", out var descElement))
                                aiDesc = descElement.GetString() ?? "";

                            if (!string.IsNullOrWhiteSpace(skillCode))
                            {
                                builtins.Add(new
                                {
                                    SkillCode = skillCode,
                                    AIDesc = aiDesc
                                });
                            }
                        }
                    }
                }

                return Ok(ResponseHelper.Success(new
                {
                    workflows, // 优先直接调用
                    builtins   // 没有现成技能时再组合使用
                }));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json 格式错误");
                return StatusCode(500, ResponseHelper.Fail<object>("skill-manifest.json 格式错误"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 技能列表失败");
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能列表失败"));
            }
        }




        /// <summary>
        /// 返回系统内置原子技能说明（manifest）
        ///
        /// 说明：
        /// 1. manifest 放在 Config/skill-manifest.json
        /// 2. 后续新增/修改 builtin skill 时，只改 JSON 文件即可
        /// 3. AI 读取这个接口后，就知道 browser_task / file_task / email_task 等怎么调用
        /// </summary>
        [HttpPost("GetBuiltinSkillManifest")]
        public IActionResult GetBuiltinSkillManifest()
        {
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "Config", "skill-manifest.json");

                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("未找到内置技能 manifest 文件：{FilePath}", filePath);
                    return NotFound(ResponseHelper.Fail<object>("skill-manifest.json 不存在"));
                }

                var json = System.IO.File.ReadAllText(filePath, Encoding.UTF8);

                using var doc = JsonDocument.Parse(json);
                var data = doc.RootElement.Clone();

                return Ok(ResponseHelper.Success(data));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json 格式错误");
                return StatusCode(500, ResponseHelper.Fail<object>("skill-manifest.json 格式错误"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取内置技能 manifest 失败");
                return StatusCode(500, ResponseHelper.Fail<object>("读取内置技能 manifest 失败"));
            }
        }


        /// <summary>
        /// 获取某个 workflow 技能的详细定义
        ///
        /// 适合 AI 使用：
        /// 1. 先通过 GetSkillListForAI 确认有哪些 workflow
        /// 2. 再通过本接口读取某个 workflow 的具体步骤
        /// 3. 如果 skillCode 不存在，会明确返回失败
        /// 4. 如果 SkillActions JSON 格式错误，会明确返回失败
        /// </summary>
        [HttpPost("GetSkillAction")]
        public async Task<IActionResult> GetSkillAction([FromBody] SkillModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            try
            {
                var sql = @"
                            SELECT SkillCode, SkillActions, Remark, SkillType, UpdateTime
                            FROM Skills
                            WHERE SkillCode = @SkillCode
                            LIMIT 1";

                var skill = await QueryFirstOrDefaultAsync<dynamic>(sql, new { SkillCode = model.SkillCode });

                if (skill == null)
                {
                    _logger.LogWarning("未找到技能定义：{SkillCode}", model.SkillCode);
                    return NotFound(ResponseHelper.Fail<object>("未找到该技能"));
                }

                string skillCode = skill.SkillCode?.ToString() ?? "";
                string skillActions = skill.SkillActions?.ToString() ?? "";
                string remark = skill.Remark?.ToString() ?? "";
                string skillType = skill.SkillType?.ToString() ?? "";
                string updateTime = skill.UpdateTime?.ToString() ?? "";

                List<SkillStep> steps;
                try
                {
                    steps = string.IsNullOrWhiteSpace(skillActions)
                        ? new List<SkillStep>()
                        : JsonSerializer.Deserialize<List<SkillStep>>(skillActions) ?? new List<SkillStep>();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "技能动作 JSON 格式错误，SkillCode={SkillCode}", model.SkillCode);
                    return StatusCode(500, ResponseHelper.Fail<object>("SkillActions JSON 格式错误"));
                }

                return Ok(ResponseHelper.Success(new
                {
                    skillCode,
                    remark,
                    skillType,
                    updateTime,

                    // 原始 JSON，便于客户端直接使用
                    skillActionsRaw = skillActions,

                    // 解析后的步骤，便于 AI 理解
                    steps
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能详情失败，SkillCode={SkillCode}", model.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能详情失败"));
            }
        }



        #endregion


        #region 执行入口

        /// <summary>
        /// 统一执行入口
        ///
        /// 执行顺序：
        /// 1. 如果请求里直接带了 Steps，则按临时 workflow 执行
        /// 2. 否则先查数据库是否存在同名 workflow 技能
        /// 3. 如果有，则按数据库 workflow 执行
        /// 4. 如果没有，则按内置原子技能执行
        /// </summary>
        [HttpPost("ExecuteSkill")]
        public async Task<IActionResult> ExecuteSkill([FromBody] ExecSkillModel model)
        {
            if (model == null)
                return BadRequest(ResponseHelper.Fail<object>("请求体不能为空"));

            string code = model.SkillCode?.Trim() ?? "";
            var args = model.Arguments ?? new Dictionary<string, object>();

            try
            {
                object result;
                string executeMode;

                // 1. 优先执行请求中直接传入的临时 workflow
                if (model.Steps != null && model.Steps.Count > 0)
                {
                    _logger.LogInformation("开始执行临时 workflow，SkillCode={SkillCode}", code);
                    result = await RunWorkflowAsync(model.Steps, args);
                    executeMode = "temp_workflow";
                }
                else
                {
                    // 2. 再尝试读取数据库中的 workflow 技能定义
                    var skillJson = await QueryFirstOrDefaultAsync<string>(
                        "SELECT SkillActions FROM Skills WHERE SkillCode = @SkillCode LIMIT 1",
                        new { SkillCode = code });

                    if (!string.IsNullOrWhiteSpace(skillJson))
                    {
                        List<SkillStep> steps;
                        try
                        {
                            steps = JsonSerializer.Deserialize<List<SkillStep>>(skillJson) ?? new List<SkillStep>();
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "SkillActions JSON 格式错误，SkillCode={SkillCode}", code);
                            return StatusCode(500, ResponseHelper.Fail<object>("SkillActions JSON 格式错误"));
                        }

                        _logger.LogInformation("开始执行 workflow 技能，SkillCode={SkillCode}", code);
                        result = await RunWorkflowAsync(steps, args);
                        executeMode = "workflow";
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(code))
                            return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空，或者请直接传 Steps"));

                        _logger.LogInformation("开始执行 builtin 技能，SkillCode={SkillCode}", code);
                        result = await ExecuteSkillInternal(code, args);
                        executeMode = "builtin";
                    }
                }

                return Ok(ResponseHelper.Success(new
                {
                    skillCode = code,
                    executeMode, // temp_workflow / workflow / builtin
                    result
                }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "安全限制，SkillCode={SkillCode}，Message={Message}", code, ex.Message);
                return StatusCode(403, ResponseHelper.Fail<object>(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "文件不存在，SkillCode={SkillCode}，Message={Message}", code, ex.Message);
                return NotFound(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "不支持的技能，SkillCode={SkillCode}，Message={Message}", code, ex.Message);
                return BadRequest(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行技能异常，SkillCode={SkillCode}", code);
                return StatusCode(500, ResponseHelper.Fail<object>("服务器内部错误"));
            }
        }



        #endregion

        #region 工作流引擎

        /// <summary>
        /// 执行 workflow 技能
        ///
        /// 功能：
        /// 1. 按顺序执行每个步骤
        /// 2. 支持模板变量，例如：
        ///    {{step0}}
        ///    {{step0.path}}
        ///    {{step0.data.path}}
        /// 3. 每一步执行结果会写入上下文，供后续步骤引用
        ///
        /// 返回：
        /// - success: 是否成功
        /// - msg: 执行结果说明
        /// - totalSteps: 总步骤数
        /// - completedSteps: 已完成步骤数
        /// - failedAt: 失败步骤索引（失败时返回）
        /// - failedStep: 失败步骤名称（失败时返回）
        /// - lastResult: 最后一个成功步骤的结果
        /// - log: 每一步的执行日志
        /// </summary>
        private async Task<object> RunWorkflowAsync(List<SkillStep>? steps, Dictionary<string, object>? input)
        {
            // 1. 空值保护
            var safeSteps = steps ?? new List<SkillStep>();
            var context = input != null
                ? new Dictionary<string, object>(input)
                : new Dictionary<string, object>();

            var log = new List<object>();
            object? lastResult = null;

            // 2. 没有步骤时直接返回
            if (safeSteps.Count == 0)
            {
                return new
                {
                    success = true,
                    msg = "workflow 没有可执行步骤",
                    totalSteps = 0,
                    completedSteps = 0,
                    lastResult = (object?)null,
                    log
                };
            }

            // 3. 逐步执行
            for (int i = 0; i < safeSteps.Count; i++)
            {
                var step = safeSteps[i];

                // 防止空步骤导致异常
                var action = step?.Action?.Trim() ?? "";
                var stepArgs = step?.Args ?? new Dictionary<string, object>();

                if (string.IsNullOrWhiteSpace(action))
                {
                    log.Add(new
                    {
                        stepIndex = i,
                        step = "",
                        success = false,
                        error = "步骤 Action 不能为空"
                    });

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = "",
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult,
                        log
                    };
                }

                // 先解析模板变量
                Dictionary<string, object> resolvedArgs;
                try
                {
                    resolvedArgs = ResolveTemplateVariables(stepArgs, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "步骤参数模板解析失败，StepIndex={StepIndex}，Action={Action}", i, action);

                    log.Add(new
                    {
                        stepIndex = i,
                        step = action,
                        success = false,
                        error = "参数模板解析失败：" + ex.Message
                    });

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = action,
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult,
                        log
                    };
                }

                try
                {
                    _logger.LogInformation("开始执行 workflow 步骤，StepIndex={StepIndex}，Action={Action}", i, action);

                    // 执行当前原子技能
                    var result = await ExecuteSkillInternal(action, resolvedArgs);

                    // 保存当前步骤结果
                    context[$"step{i}"] = result;
                    lastResult = result;

                    log.Add(new
                    {
                        stepIndex = i,
                        step = action,
                        success = true,
                        args = resolvedArgs,
                        result
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "workflow 步骤执行失败，StepIndex={StepIndex}，Action={Action}", i, action);

                    log.Add(new
                    {
                        stepIndex = i,
                        step = action,
                        success = false,
                        args = resolvedArgs,
                        error = ex.Message
                    });

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = action,
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult,
                        log
                    };
                }
            }

            // 4. 全部执行完成
            return new
            {
                success = true,
                msg = "workflow 执行完成",
                totalSteps = safeSteps.Count,
                completedSteps = safeSteps.Count,
                lastResult,
                log
            };
        }


        /// <summary>
        /// 统一执行内置原子技能
        ///
        /// 说明：
        /// 1. 这里只处理 builtin skill
        /// 2. workflow skill 不走这里，而是走 RunWorkflowAsync
        /// 3. 如果 skillCode 不支持，会抛 NotSupportedException
        /// </summary>
        private async Task<object> ExecuteSkillInternal(string skillCode, Dictionary<string, object>? args)
        {
            var code = skillCode?.Trim().ToLowerInvariant() ?? "";
            var safeArgs = args ?? new Dictionary<string, object>();

            return code switch
            {
                "file_task" => await DoFileTaskAsync(safeArgs),
                "open_task" => await DoOpenTaskAsync(safeArgs),
                "print_task" => await DoPrintTaskAsync(safeArgs),
                "folder_task" => await DoFolderTaskAsync(safeArgs),
                "tool_task" => await RunExternalToolAsync(safeArgs),
                "lock_task" => await CommandTools.LockScreenAsync(),
                "screenshot_task" => await CommandTools.CaptureScreenAsync(),
                "email_task" => await CommandTools.SendEmailAsync(safeArgs),
                "browser_task" => await DoBrowserTaskAsync(safeArgs),
                _ => throw new NotSupportedException($"不支持的技能：{skillCode}")
            };
        }



        #region 模板处理
        /// <summary>
        /// 模板变量替换
        ///
        /// 支持：
        /// - {{step0}}
        /// - {{step0.path}}
        /// - {{step0.data.path}}
        /// - {{myInputVar}}
        ///
        /// 说明：
        /// 1. 只对“可转成字符串”的参数做模板替换
        /// 2. 非字符串类型的值原样保留
        /// 3. 如果模板变量不存在，替换为空字符串
        /// 4. 支持从：
        ///    - Dictionary<string, object>
        ///    - JsonElement
        ///    - 匿名对象 / 普通对象属性
        ///    中读取字段
        /// </summary>
        private Dictionary<string, object> ResolveTemplateVariables(
            Dictionary<string, object>? args,
            IReadOnlyDictionary<string, object> context)
        {
            // 1. 空值保护
            if (args == null || args.Count == 0)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in args)
            {
                // 2. 尝试把当前参数转成字符串
                //    只有字符串类参数才做模板替换
                string? rawText = kv.Value switch
                {
                    null => null,
                    string s => s,
                    JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                    JsonElement je => je.ToString(),
                    _ => kv.Value.ToString()
                };

                // 3. 如果无法转成字符串，则原样保留
                if (rawText == null)
                {
                    resolved[kv.Key] = kv.Value!;
                    continue;
                }

                // 4. 替换模板变量
                //    例如：{{step0.data.path}}
                var replaced = Regex.Replace(rawText, @"\{\{\s*([^}]+?)\s*\}\}", match =>
                {
                    var expr = match.Groups[1].Value.Trim();
                    return ResolveTemplateExpression(expr, context);
                });

                resolved[kv.Key] = replaced;
            }

            return resolved;
        }

        /// <summary>
        /// 解析单个模板表达式
        ///
        /// 示例：
        /// - step0
        /// - step0.path
        /// - step0.data.path
        /// - myInputVar
        ///
        /// 规则：
        /// 1. 先取第一级变量（如 step0 / myInputVar）
        /// 2. 再逐级读取属性/字段
        /// 3. 任意一级不存在时返回空字符串
        /// </summary>
        private string ResolveTemplateExpression(
            string expr,
            IReadOnlyDictionary<string, object> context)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return "";

            // 按点分隔，例如 step0.data.path
            var parts = expr.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return "";

            // 先从上下文中取第一级变量
            if (!context.TryGetValue(parts[0], out var current) || current == null)
                return "";

            // 逐级往下取值
            for (int i = 1; i < parts.Length; i++)
            {
                current = GetObjectMemberValue(current, parts[i]);
                if (current == null)
                    return "";
            }

            return ConvertObjectToString(current);
        }

        /// <summary>
        /// 从对象中读取指定成员值
        ///
        /// 支持：
        /// 1. Dictionary<string, object>
        /// 2. JsonElement（对象类型）
        /// 3. 匿名对象 / 普通对象属性
        /// </summary>
        private object? GetObjectMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            // 1. Dictionary<string, object>
            if (obj is IDictionary<string, object> dict)
            {
                return dict.TryGetValue(memberName, out var value) ? value : null;
            }

            // 2. JsonElement
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object &&
                    je.TryGetProperty(memberName, out var child))
                {
                    return child;
                }

                return null;
            }

            // 3. 普通对象 / 匿名对象
            var prop = obj.GetType().GetProperty(memberName);
            if (prop != null)
                return prop.GetValue(obj);

            return null;
        }

        /// <summary>
        /// 把 object 安全转成字符串
        ///
        /// 支持：
        /// - string
        /// - JsonElement
        /// - 普通对象
        /// </summary>
        private string ConvertObjectToString(object? value, string defaultValue = "")
        {
            if (value == null)
                return defaultValue;

            return value switch
            {
                string s => s,
                JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString() ?? defaultValue,
                JsonElement je => je.ToString() ?? defaultValue,
                _ => value.ToString() ?? defaultValue
            };
        }


        #endregion

        #endregion

        #region 原子技能：文件

       
        /// <summary>
        /// 文件类技能：
        /// search / copy / move / rename / mkdir
        /// </summary>
        private async Task<object> DoFileTaskAsync(Dictionary<string, object> args)
        {
            string action = GetString(args, "action");
            string from = GetString(args, "from");
            string to = GetString(args, "to");
            string keyword = GetString(args, "keyword");
            string ext = GetString(args, "ext", "*");
            string newName = GetString(args, "newName");

            return action.Trim().ToLowerInvariant() switch
            {
                "search" => await SearchFileAsync(keyword, ext),
                "copy" => await CopyAsync(from, to),
                "move" => await MoveAsync(from, to),
                "rename" => await RenameAsync(from, newName),
                "mkdir" => await CreateDirAsync(from),
                _ => throw new NotSupportedException($"file_task 不支持的操作：{action}")
            };
        }



        private async Task<object> DoOpenTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("路径不能为空");

            var fullPath = ValidatePath(path, mustExist: true);

            if (!System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"路径不存在: {fullPath}");

            await Task.Run(() =>
            {
                using var p = Process.Start(new ProcessStartInfo(fullPath)
                {
                    UseShellExecute = true
                });
            });

            return "已打开：" + fullPath;
        }

        private async Task<object> DoPrintTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("文件路径不能为空");

            var fullPath = ValidatePath(path, mustExist: true);
            if (!System.IO.File.Exists(fullPath))
                throw new FileNotFoundException($"文件不存在: {fullPath}");

            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo(fullPath)
                {
                    Verb = "Print",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(10000);
            });

            return "已发送打印：" + Path.GetFileName(fullPath);
        }

        private async Task<object> DoFolderTaskAsync(Dictionary<string, object> args)
        {
            string source = GetString(args, "source");
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("源目录不能为空");

            var fullSource = ValidatePath(source, mustExist: true);
            if (!Directory.Exists(fullSource))
                throw new DirectoryNotFoundException($"目录不存在: {fullSource}");

            int count = 0;

            await Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(fullSource))
                {
                    try
                    {
                        string ext = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
                        string targetDir = Path.Combine(fullSource, string.IsNullOrEmpty(ext) ? "无后缀" : ext);

                        Directory.CreateDirectory(targetDir);

                        string targetPath = Path.Combine(targetDir, Path.GetFileName(file));
                        System.IO.File.Move(file, targetPath);

                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "归类文件失败: {FilePath}", file);
                    }
                }
            });

            return $"归类完成，共移动 {count} 个文件";
        }

        #endregion

        #region 原子技能：浏览器

        /// <summary>
        /// 浏览器技能
        ///
        /// 参数：
        /// {
        ///   "actions": [ ...BrowserAction数组... ]
        /// }
        ///
        /// 说明：
        /// 1. 这里不再直接调用 BrowserController
        /// 2. 而是调用 BrowserService.ExecuteActionAsync
        /// 3. 最终返回最后一步结果，供工作流引用
        /// </summary>
        private async Task<object> DoBrowserTaskAsync(Dictionary<string, object> args)
        {
            List<BrowserAction> actions;

            if (!args.TryGetValue("actions", out var actionsObj) || actionsObj == null)
                throw new ArgumentException("browser_task 必须提供 actions");

            // actions 既可能是 JSON 字符串，也可能是 JsonElement
            if (actionsObj is string actionsJson)
            {
                actions = JsonSerializer.Deserialize<List<BrowserAction>>(actionsJson) ?? new List<BrowserAction>();
            }
            else if (actionsObj is JsonElement je)
            {
                actions = JsonSerializer.Deserialize<List<BrowserAction>>(je.GetRawText()) ?? new List<BrowserAction>();
            }
            else
            {
                throw new ArgumentException("actions 格式不正确，必须是 JSON 数组");
            }

            if (actions.Count == 0)
                throw new ArgumentException("browser_task 的 actions 不能为空");

            var session = await _browserService.CreateSessionAsync();
            var outputs = new List<BrowserActionResult>();

            try
            {
                // 同一个 session 内部动作串行执行
                await session.ActionLock.WaitAsync();

                try
                {
                    foreach (var action in actions)
                    {
                        var result = await _browserService.ExecuteActionAsync(session, action);
                        outputs.Add(result);
                    }
                }
                finally
                {
                    session.ActionLock.Release();
                }

                // 返回最后一步结果，便于后续工作流引用 stepX.data.path 等字段
                return outputs.LastOrDefault() ?? new BrowserActionResult
                {
                    Success = true,
                    Type = "browser_task",
                    Text = "浏览器动作执行完成，但没有输出"
                };
            }
            catch
            {
                // 这里不主动关闭 session，是为了保留后续扩展空间（例如会话复用）
                throw;
            }
        }

        #endregion

        #region 原子技能：外部工具

        /// <summary>
        /// 调用白名单中的外部 exe
        /// </summary>
        private async Task<object> RunExternalToolAsync(Dictionary<string, object> args)
        {
            string exePath = GetString(args, "exePath");
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("工具路径不能为空");

            string exeName = Path.GetFileName(exePath);
            if (!AllowedExeNames.Contains(exeName))
                throw new UnauthorizedAccessException($"工具 {exeName} 未在白名单中");

            var fullExePath = ValidatePath(exePath, mustExist: true);
            if (!System.IO.File.Exists(fullExePath))
                throw new FileNotFoundException($"工具不存在: {fullExePath}");

            string arguments = GetString(args, "arguments");
            int timeoutSec = int.TryParse(GetString(args, "timeout", "10"), out int t) ? t : 10;
            timeoutSec = Math.Clamp(timeoutSec, 1, 60);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fullExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(true);
                }
                catch
                {
                    // 忽略 kill 失败
                }

                throw new TimeoutException($"外部工具执行超时（{timeoutSec}秒）");
            }

            string output = await outputTask;
            string error = await errorTask;

            return new
            {
                status = "完成",
                exitCode = process.ExitCode,
                output = string.IsNullOrWhiteSpace(output) ? "无输出" : output,
                error = string.IsNullOrWhiteSpace(error) ? "无错误" : error
            };
        }

        #endregion

        #region 文件搜索

        /// <summary>
        /// 搜索文件：
        /// 优先 Everything → 降级 Windows Search → 最后递归搜索
        /// </summary>
        private async Task<string> SearchFileAsync(string keyword, string ext = "*")
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("搜索关键词不能为空");

            try
            {
                var resultList = await SearchWithEverythingAsync(keyword, ext);
                if (resultList.Any())
                    return FormatFileList(resultList);
            }
            catch
            {
                try
                {
                    var resultList = await SearchWithWindowsSearchAsync(keyword, ext);
                    if (resultList.Any())
                        return FormatFileList(resultList);
                }
                catch
                {
                    var resultList = await SearchFallbackAsync(keyword, ext);
                    return resultList.Any() ? FormatFileList(resultList) : "未找到文件";
                }
            }

            return "未找到文件";
        }

        /// <summary>
        /// Everything 搜索
        /// </summary>
        private async Task<List<string>> SearchWithEverythingAsync(string keyword, string ext = "*")
        {
            var list = new List<string>();
            var searchClient = new EverythingSearchClient.SearchClient();

            var results = await Task.Run(() => searchClient.Search(keyword));
            if (results.Items == null || results.Items.Length == 0)
                return list;

            foreach (var item in results.Items)
            {
                try
                {
                    string path = item.Path;

                    if (!string.IsNullOrWhiteSpace(ext) &&
                        ext != "*" &&
                        !path.EndsWith($".{ext.Trim('.')}", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ValidatePath(path, mustExist: true);
                    list.Add(path);
                }
                catch
                {
                    // 忽略非法路径/不可访问项
                }
            }

            return list;
        }

        /// <summary>
        /// Windows Search 搜索
        /// </summary>
        private async Task<List<string>> SearchWithWindowsSearchAsync(string keyword, string ext = "*")
        {
            var list = new List<string>();
            string safeKeyword = keyword.Replace("\"", "\"\"");

            string query = $@"
                                SELECT System.ItemPathDisplay
                                FROM SYSTEMINDEX
                                WHERE CONTAINS(System.FileName, ""{safeKeyword}"")";

            if (!string.IsNullOrWhiteSpace(ext) && ext != "*")
                query += $" AND System.FileExtension = '.{ext.TrimStart('.')}'";

            query += " ORDER BY System.DateModified DESC";

            using var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            using var cmd = new OleDbCommand(query, conn);

            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();
            while (reader != null && await reader.ReadAsync())
            {
                try
                {
                    string path = reader.GetString(0);
                    ValidatePath(path, mustExist: true);
                    list.Add(path);
                }
                catch
                {
                    // 忽略非法路径/不可访问项
                }
            }

            return list;
        }

        /// <summary>
        /// 兜底递归搜索
        /// </summary>
        private async Task<List<string>> SearchFallbackAsync(string keyword, string ext = "*")
        {
            var list = new List<string>();

            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                @"D:\",
                @"E:\"
            };

            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir))
                    continue;

                try
                {
                    var pattern = ext == "*" ? "*.*" : $"*.{ext.Trim('.')}";

                    var files = Directory.EnumerateFiles(dir, pattern, options)
                        .Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        try
                        {
                            ValidatePath(file, mustExist: true);
                            list.Add(file);
                        }
                        catch
                        {
                            // 忽略非法路径/不可访问项
                        }
                    }
                }
                catch
                {
                    // 忽略单个目录搜索失败
                }
            }

            return await Task.FromResult(list);
        }

        /// <summary>
        /// 文件列表格式化为多行文本，方便 AI 阅读
        /// </summary>
        private string FormatFileList(List<string> files)
        {
            if (files == null || files.Count == 0)
                return "未找到文件";

            var sb = new StringBuilder();
            sb.AppendLine($"找到 {files.Count} 个文件：");

            foreach (var file in files)
                //sb.AppendLine("✅ " + file);
                sb.AppendLine(file);

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region 安全文件操作

        /// <summary>
        /// 路径安全校验
        ///
        /// 说明：
        /// 1. 只允许访问 AllowedRoots 中的路径
        /// 2. 可选是否要求路径必须存在
        /// </summary>
        private string ValidatePath(string inputPath, bool mustExist = false)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("路径不能为空");

            string fullPath = Path.GetFullPath(inputPath);

            var allowedRoots = _fsOptions.AllowedRoots ?? new List<string>();

            bool allowed = allowedRoots.Any(root =>
            {
                var fullRoot = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                return fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
            });

            if (!allowed)
                throw new UnauthorizedAccessException("路径不在允许范围");

            if (mustExist && !System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"不存在: {fullPath}");

            return fullPath;
        }

        private async Task<SkillResult> CopyAsync(string from, string to)
        {
            var source = ValidatePath(from, mustExist: true);
            var target = ValidatePath(to, mustExist: false);

            await Task.Run(() =>
            {
                var targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                System.IO.File.Copy(source, target, overwrite: true);
            });

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "copy",
                Text = "已复制",
                Data = new
                {
                    from = source,
                    to = target
                }
            };
        }


        private async Task<SkillResult> MoveAsync(string from, string to)
        {
            var source = ValidatePath(from, mustExist: true);
            var target = ValidatePath(to, mustExist: false);

            await Task.Run(() =>
            {
                var targetDir = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(targetDir))
                    Directory.CreateDirectory(targetDir);

                if (System.IO.File.Exists(target))
                    System.IO.File.Delete(target);

                System.IO.File.Move(source, target);
            });

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "move",
                Text = "已移动",
                Data = new
                {
                    from = source,
                    to = target
                }
            };
        }

        private async Task<SkillResult> RenameAsync(string path, string newName)
        {
            var source = ValidatePath(path, mustExist: true);

            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("新文件名不能为空");

            // 确保 newName 不包含路径，只取文件名
            var safeNewName = Path.GetFileName(newName);

            var sourceDir = Path.GetDirectoryName(source);
            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentException("无法获取源文件目录");

            var target = Path.Combine(sourceDir, safeNewName);
            var targetValidated = ValidatePath(target, mustExist: false);

            await Task.Run(() =>
            {
                if (System.IO.File.Exists(target))
                    System.IO.File.Delete(target);

                System.IO.File.Move(source, target);
            });

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "rename",
                Text = "已重命名",
                Data = new
                {
                    from = source,
                    to = target
                }
            };
        }


        private async Task<SkillResult> CreateDirAsync(string path)
        {
            var fullPath = ValidatePath(path, mustExist: false);
            await Task.Run(() => Directory.CreateDirectory(fullPath));

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "mkdir",
                Text = "已创建目录",
                Data = new
                {
                    path = fullPath
                }
            };
        }


        #endregion

        #region 工具方法

        /// <summary>
        /// 从 Dictionary<string, object> 中安全取字符串
        /// 兼容 string / JsonElement / 普通 object
        /// </summary>
        private new string GetString(Dictionary<string, object>? args, string key, string defaultValue = "")
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            return ConvertObjectToString(value, defaultValue).Trim();
        }

        

        #endregion

        #region 管理接口

        [HttpPost("GetAllSkillCodes")]
        public async Task<IActionResult> GetAllSkillCodes()
        {
            var codes = await QueryAsync<string>("SELECT SkillCode FROM Skills");
            return Ok(ResponseHelper.Success(codes));
        }

        



        /// <summary>
        /// 保存技能定义
        /// 存在则更新，不存在则插入
        /// </summary>
        [HttpPost("SaveSkillAction")]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            var exists = await QueryFirstOrDefaultAsync<int>(
                "SELECT 1 FROM Skills WHERE SkillCode = @SkillCode LIMIT 1",
                model);

            if (exists == 1)
            {
                await ExecuteAsync(@"
UPDATE Skills
SET SkillActions = @SkillActions,
    Remark = @Remark,
    SkillType = @SkillType,
    UpdateTime = @UpdateTime
WHERE SkillCode = @SkillCode", model);
            }
            else
            {
                await ExecuteAsync(@"
INSERT INTO Skills (SkillCode, SkillActions, Remark, SkillType, UpdateTime)
VALUES (@SkillCode, @SkillActions, @Remark, @SkillType, @UpdateTime)", model);
            }

            return Ok(ResponseHelper.Success("保存成功"));
        }

        [HttpPost("GetSkillList")]
        public async Task<IActionResult> GetSkillList()
        {
            var list = await QueryAsync<dynamic>("SELECT * FROM Skills");
            return Ok(ResponseHelper.Success(list));
        }

        [HttpPost("DeleteSkill")]
        public async Task<IActionResult> DeleteSkill([FromBody] SkillModel model)
        {
            if (model == null || (model.ID <= 0 && string.IsNullOrWhiteSpace(model.SkillCode)))
                return BadRequest();

            string sql = model.ID > 0
                ? "DELETE FROM Skills WHERE ID = @ID"
                : "DELETE FROM Skills WHERE SkillCode = @SkillCode";

            await ExecuteAsync(sql, model);
            return Ok(ResponseHelper.Success("删除成功"));
        }

        [HttpPost("ExecSql")]
        public IActionResult ExecSql()
        {
            return StatusCode(403, ResponseHelper.Fail<object>("禁用"));
        }

        #endregion
    }

    #region 模型

    public class SkillModel
    {
        public int ID { get; set; }

        public string SkillCode { get; set; } = "";

        public string SkillActions { get; set; } = "";

        public string Remark { get; set; } = "";

        /// <summary>
        /// 技能类型，前端不传时默认 OtherType
        /// </summary>
        public string SkillType { get; set; } = "OtherType";

        /// <summary>
        /// 更新时间，当前先保留字符串格式，兼容你现有库表
        /// </summary>
        public string UpdateTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public class ExecSkillModel
    {
        public string SkillCode { get; set; } = "";

        public Dictionary<string, object> Arguments { get; set; } = new();

        /// <summary>
        /// 临时 workflow 步骤。
        /// 如果传了 Steps，就直接执行，不走数据库。
        /// </summary>
        public List<SkillStep> Steps { get; set; } = new();
    }


    public class SkillStep
    {
        public string Action { get; set; } = "";

        public Dictionary<string, object> Args { get; set; } = new();
    }

    public class FileSystemOptions
    {
        public List<string> AllowedRoots { get; set; } = new()
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
    }

    #endregion
}
