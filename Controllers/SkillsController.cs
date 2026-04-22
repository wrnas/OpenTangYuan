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
using TangYuan.Tools;
using System.Collections.Concurrent;
using MailKit;
using MailKit.Search;


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


        // 公共缓存：两个接口共用
        private static string? _cachedManifestJson;
        private static JsonDocument? _cachedManifestDoc;

        // 缓存锁（防止并发重复加载）
        private static readonly object _cacheLock = new();



        private class MailContextCacheItem
        {
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime LastAccessAt { get; set; } = DateTime.Now;
            public List<EmailListItemDto> Items { get; set; } = new();
        }

        private static readonly ConcurrentDictionary<string, MailContextCacheItem> _mailContext
            = new(StringComparer.OrdinalIgnoreCase);

        private const int MailContextExpireMinutes = 30;


        /// <summary>
        /// 邮件搜索结果上下文缓存
        /// 用于支持“第一封”“上一封”这类后续对话引用
        /// key 建议用 contextKey
        /// </summary>
        private static readonly ConcurrentDictionary<string, List<EmailListItemDto>> _mailContextCache = new(StringComparer.OrdinalIgnoreCase);


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



        #region 内部处理
        /// <summary>
        /// browser_task 反序列化浏览器动作时使用的 JSON 配置
        /// 说明：
        /// 1. 允许小写字段映射到 BrowserAction 的大写属性
        /// 2. 允许数字字段以字符串形式传入，例如 "take": "10"
        /// </summary>
        private static readonly JsonSerializerOptions BrowserJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        #endregion

        #region AI技能相关        

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
                var sql = "SELECT SkillCode, Remark AS AIDesc FROM Skills ORDER BY ID ASC";
                var workflowsRaw = (await QueryAsync<dynamic>(sql)).ToList();

                var workflows = workflowsRaw.Select(x => new
                {
                    skillCode = x.SkillCode?.ToString() ?? "",
                    AIDesc = x.AIDesc?.ToString() ?? "",
                    sourceType = "workflow",
                    needDetail = true
                }).ToList();

                var builtins = new List<object>();

                try
                {
                    var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                    if (System.IO.File.Exists(filePath))
                    {
                        lock (_cacheLock)
                        {
                            if (_cachedManifestDoc == null)
                            {
                                byte[] jsonBytes = System.IO.File.ReadAllBytes(filePath);
                                _cachedManifestDoc = JsonDocument.Parse(jsonBytes);
                                _cachedManifestJson = null;
                            }
                        }

                        var root = _cachedManifestDoc.RootElement;
                        JsonElement builtinsElement;

                        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("builtins", out var builtinsProp))
                        {
                            builtinsElement = builtinsProp;
                        }
                        else if (root.ValueKind == JsonValueKind.Array)
                        {
                            builtinsElement = root;
                        }
                        else
                        {
                            builtinsElement = default;
                        }

                        if (builtinsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in builtinsElement.EnumerateArray())
                            {
                                if (item.ValueKind != JsonValueKind.Object)
                                    continue;

                                string skillCode = item.TryGetProperty("skillCode", out var skillCodeProp)
                                    ? (skillCodeProp.GetString() ?? "")
                                    : "";

                                string aiDesc = item.TryGetProperty("AIDesc", out var descProp)
                                    ? (descProp.GetString() ?? "")
                                    : "";

                                if (!string.IsNullOrWhiteSpace(skillCode))
                                {
                                    builtins.Add(new
                                    {
                                        skillCode,
                                        AIDesc = aiDesc,
                                        sourceType = "builtin",
                                        needDetail = true
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "读取内置技能目录失败，builtins 将返回空数组");
                }

                return Ok(ResponseHelper.Success(new
                {
                    workflows,
                    builtins
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 技能列表失败");
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能列表失败"));
            }
        }



        #region  GetBuiltinSkillDetail  获取内部定义详情
        [HttpPost("GetBuiltinSkillDetail")]
        public IActionResult GetBuiltinSkillDetail([FromBody] SkillBaseModel request)
        {
            if (string.IsNullOrWhiteSpace(request.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("未找到内置技能 manifest 文件：{FilePath}", filePath);
                    return NotFound(ResponseHelper.Fail<object>("skill-manifest.json 不存在"));
                }

                lock (_cacheLock)
                {
                    if (_cachedManifestDoc == null)
                    {
                        byte[] jsonBytes = System.IO.File.ReadAllBytes(filePath);
                        _cachedManifestDoc = JsonDocument.Parse(jsonBytes);
                        _cachedManifestJson = null;
                    }
                }

                var root = _cachedManifestDoc.RootElement;
                JsonElement builtins;

                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("builtins", out var builtinsProp))
                {
                    builtins = builtinsProp;
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    builtins = root;
                }
                else
                {
                    return NotFound(ResponseHelper.Fail<object>("manifest 中未找到 builtins"));
                }

                foreach (var item in builtins.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;

                    if (item.TryGetProperty("skillCode", out var skillCodeProp) &&
                        string.Equals(skillCodeProp.GetString(), request.SkillCode, StringComparison.OrdinalIgnoreCase))
                    {
                        return Ok(ResponseHelper.Success(item.Clone()));
                    }
                }

                return NotFound(ResponseHelper.Fail<object>("未找到该 builtin skill"));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json 格式错误");
                return StatusCode(500, ResponseHelper.Fail<object>("skill-manifest.json 格式错误"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 builtin skill 详情失败，SkillCode={SkillCode}", request.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("读取 builtin skill 详情失败"));
            }
        }

        #endregion


        #region 保留备用
        [HttpPost("GetSkillListWithBuiltinForAI")]
        public async Task<IActionResult> GetSkillListWithBuiltinForAI()
        {
            try
            {
                // 1. 数据库技能
                var sql = "SELECT SkillCode, Remark AS AIDesc FROM Skills ORDER BY ID ASC";
                var workflows = (await QueryAsync<dynamic>(sql)).ToList();

                // 2. 内置原子技能（复用缓存）
                JsonElement builtins = default;
                try
                {
                    var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                    if (System.IO.File.Exists(filePath))
                    {
                        lock (_cacheLock)
                        {
                            if (_cachedManifestDoc == null)
                            {
                                byte[] jsonBytes = System.IO.File.ReadAllBytes(filePath);
                                _cachedManifestDoc = JsonDocument.Parse(jsonBytes);
                            }
                            // 克隆一份，避免外部修改
                            builtins = _cachedManifestDoc.RootElement.Clone();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("skill-manifest.json 不存在，builtins 将返回空对象");
                        builtins = JsonDocument.Parse("{}").RootElement.Clone();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "读取 skill-manifest.json 失败，builtins 将返回空对象");
                    builtins = JsonDocument.Parse("{}").RootElement.Clone();
                }

                return Ok(ResponseHelper.Success(new
                {
                    workflows,
                    builtins
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 技能列表失败");
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能列表失败"));
            }
        }
        #endregion

        /// <summary>
        /// 返回系统内置原子技能说明（manifest）
        /// </summary>        
        [HttpPost("GetBuiltinSkillManifest")]
        public IActionResult GetBuiltinSkillManifest()
        {
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("未找到内置技能 manifest 文件：{FilePath}", filePath);
                    return NotFound(ResponseHelper.Fail<object>("skill-manifest.json 不存在"));
                }

                // 全局缓存，只加载一次（使用字节数组，自动处理 BOM）
                lock (_cacheLock)
                {
                    if (_cachedManifestDoc == null)
                    {
                        byte[] jsonBytes = System.IO.File.ReadAllBytes(filePath);
                        _cachedManifestDoc = JsonDocument.Parse(jsonBytes);
                        _cachedManifestJson = null; // 不再使用字符串缓存
                    }
                }

                var data = _cachedManifestDoc.RootElement.Clone();
                return Ok(ResponseHelper.Success(data));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json 格式错误 at Line {Line}, Pos {Pos}", ex.LineNumber, ex.BytePositionInLine);
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
        public async Task<IActionResult> GetSkillAction([FromBody] SkillBaseModel request)
        {
            if (string.IsNullOrWhiteSpace(request.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            try
            {
                var sql = @"
            SELECT SkillCode, SkillActions, Remark, SkillType, UpdateTime
            FROM Skills
            WHERE SkillCode = @SkillCode
            LIMIT 1";

                var skill = await QueryFirstOrDefaultAsync<dynamic>(sql, new { SkillCode = request.SkillCode });

                if (skill == null)
                {
                    _logger.LogWarning("未找到技能定义：{SkillCode}", request.SkillCode);
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
                    _logger.LogError(ex, "技能动作 JSON 格式错误，SkillCode={SkillCode}", request.SkillCode);
                    return StatusCode(500, ResponseHelper.Fail<object>("SkillActions JSON 格式错误"));
                }

                return Ok(ResponseHelper.Success(new
                {
                    skillCode,
                    remark,
                    skillType,
                    updateTime,
                    skillActionsRaw = skillActions,
                    steps
                }));
            }
            catch (Exception ex)
            {
                // 🔥 这里修复成 request.SkillCode
                _logger.LogError(ex, "获取技能详情失败，SkillCode={SkillCode}", request.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能详情失败"));
            }
        }


        #endregion


        #region 执行入口

        #region 兼容coze专用

        #region 兼容coze专用

        [HttpPost("ExecuteSkillForCoze")]
        public async Task<IActionResult> ExecuteSkillForCoze([FromBody] CozeSimpleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Json))
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "缺少请求 JSON",
                    skillCode: "",
                    executeMode: "unknown",
                    errorCode: "INVALID_REQUEST",
                    errorMessage: "Json 不能为空",
                    needMoreInput: true,
                    missingArgs: new List<string> { "Json" }));
            }

            ExecSkillModel model;
            try
            {
                model = JsonSerializer.Deserialize<ExecSkillModel>(request.Json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ExecSkillModel();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Coze JSON 解析失败");
                return Ok(CozeSkillResponse.Fail(
                    message: "请求 JSON 解析失败",
                    skillCode: "",
                    executeMode: "unknown",
                    errorCode: "INVALID_JSON",
                    errorMessage: ex.Message,
                    needMoreInput: true,
                    missingArgs: new List<string> { "Json" }));
            }

            try
            {
                var (skillCode, executeMode, result) = await ExecuteSkillCoreAsync(model);
                return Ok(BuildCozeSkillResponse(skillCode, executeMode, result));
            }
            catch (ArgumentException ex)
            {
                var code = model?.SkillCode?.Trim() ?? "";
                var args = model?.Arguments ?? new Dictionary<string, object>();
                var missingArgs = TryInferMissingArgs(code, args, ex.Message);
                bool needMoreInput = missingArgs.Count > 0;

                return Ok(CozeSkillResponse.Fail(
                    message: needMoreInput ? "缺少必要参数" : "参数错误",
                    skillCode: code,
                    executeMode: "builtin",
                    errorCode: needMoreInput ? "MISSING_ARGUMENTS" : "INVALID_ARGUMENTS",
                    errorMessage: ex.Message,
                    needMoreInput: needMoreInput,
                    missingArgs: missingArgs));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "没有权限执行该技能",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "FORBIDDEN",
                    errorMessage: ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "目标文件不存在",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "FILE_NOT_FOUND",
                    errorMessage: ex.Message));
            }
            catch (NotSupportedException ex)
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "不支持的技能或操作",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "NOT_SUPPORTED",
                    errorMessage: ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteSkillForCoze 执行失败");
                return Ok(CozeSkillResponse.Fail(
                    message: "服务器内部错误",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "unknown",
                    errorCode: "INTERNAL_ERROR",
                    errorMessage: ex.Message));
            }
        }

        public class CozeSimpleRequest
        {
            public string Json { get; set; } = "";
        }

        private CozeSkillResponse BuildCozeSkillResponse(string skillCode, string executeMode, object rawResult)
        {
            // 1. builtin 结果：直接从 SkillResult 映射
            if (rawResult is SkillResult skill)
            {
                var response = new CozeSkillResponse
                {
                    Success = skill.Success,
                    Message = skill.Success ? "执行成功" : (string.IsNullOrWhiteSpace(skill.Error) ? "执行失败" : skill.Error),
                    SkillCode = string.IsNullOrWhiteSpace(skill.SkillCode) ? skillCode : skill.SkillCode,
                    ExecuteMode = executeMode,
                    ResultType = skill.Type ?? "",
                    ResultText = !string.IsNullOrWhiteSpace(skill.ResultText) ? skill.ResultText : (skill.Text ?? ""),
                    ResultList = skill.ResultList ?? new List<string>(),
                    ResultValue = skill.ResultValue ?? "",
                    ResultData = skill.Data,
                    ErrorCode = skill.Success ? "" : "SKILL_EXECUTION_FAILED",
                    ErrorMessage = skill.Error ?? ""
                };

                // 尝试补 session / page
                FillCozeExtraFields(response, skill.Data);
                return response;
            }

            // 2. workflow / temp_workflow 结果：从 lastResult 扁平化
            var responseWorkflow = new CozeSkillResponse
            {
                Success = TryGetBoolProperty(rawResult, "success"),
                Message = TryGetStringProperty(rawResult, "msg", "执行完成"),
                SkillCode = skillCode,
                ExecuteMode = executeMode,
                ResultType = "workflow",
                ResultData = rawResult
            };

            if (!responseWorkflow.Success)
            {
                responseWorkflow.ErrorCode = "WORKFLOW_EXECUTION_FAILED";
                responseWorkflow.ErrorMessage = responseWorkflow.Message;
                responseWorkflow.ResultText = responseWorkflow.Message;
                return responseWorkflow;
            }

            if (TryGetPropertyValue(rawResult, "lastResult", out var lastResultObj) && lastResultObj != null)
            {
                responseWorkflow.ResultText =
                    TryGetStringProperty(lastResultObj, "ResultText",
                    TryGetStringProperty(lastResultObj, "Text", responseWorkflow.Message));

                responseWorkflow.ResultValue =
                    TryGetStringProperty(lastResultObj, "ResultValue", "");

                if (TryGetPropertyValue(lastResultObj, "ResultList", out var resultListObj) && resultListObj != null)
                {
                    responseWorkflow.ResultList = ConvertToStringList(resultListObj);
                }

                if (TryGetPropertyValue(lastResultObj, "Data", out var innerData) && innerData != null)
                {
                    if ((responseWorkflow.ResultList == null || responseWorkflow.ResultList.Count == 0))
                    {
                        if (TryGetPropertyValue(innerData, "list", out var innerList) && innerList != null)
                        {
                            responseWorkflow.ResultList = ConvertToStringList(innerList);
                        }
                        else if (TryGetPropertyValue(innerData, "paths", out var innerPaths) && innerPaths != null)
                        {
                            responseWorkflow.ResultList = ConvertToStringList(innerPaths);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(responseWorkflow.ResultValue))
                    {
                        if (TryGetPropertyValue(innerData, "firstPath", out var firstPathObj) && firstPathObj != null)
                        {
                            responseWorkflow.ResultValue = ConvertObjectToString(firstPathObj);
                        }
                        else if (TryGetPropertyValue(innerData, "path", out var pathObj) && pathObj != null)
                        {
                            responseWorkflow.ResultValue = ConvertObjectToString(pathObj);
                        }
                        else if (TryGetPropertyValue(innerData, "result", out var resultObj) && resultObj != null &&
                                 TryGetPropertyValue(resultObj, "path", out var nestedPathObj) && nestedPathObj != null)
                        {
                            responseWorkflow.ResultValue = ConvertObjectToString(nestedPathObj);
                        }
                    }

                    FillCozeExtraFields(responseWorkflow, innerData);
                }

                if (string.IsNullOrWhiteSpace(responseWorkflow.ResultValue) &&
                    responseWorkflow.ResultList != null && responseWorkflow.ResultList.Count > 0)
                {
                    responseWorkflow.ResultValue = responseWorkflow.ResultList[0];
                }
            }
            else
            {
                responseWorkflow.ResultText = responseWorkflow.Message;
            }

            return responseWorkflow;
        }


        private void FillCozeExtraFields(CozeSkillResponse response, object? data)
        {
            if (data == null) return;

            if (string.IsNullOrWhiteSpace(response.SessionId) &&
                TryGetPropertyValue(data, "sessionId", out var sessionIdObj) && sessionIdObj != null)
            {
                response.SessionId = ConvertObjectToString(sessionIdObj);
            }

            if (response.Page == null &&
                TryGetPropertyValue(data, "page", out var pageObj) && pageObj != null)
            {
                response.Page = ParseBrowserPageState(pageObj);
            }

            if (response.Session == null &&
                TryGetPropertyValue(data, "session", out var sessionObj) && sessionObj != null)
            {
                response.Session = ParseBrowserSessionState(sessionObj);
            }
        }


        private string TryGetStringProperty(object obj, string propertyName, string defaultValue = "")
        {
            if (TryGetPropertyValue(obj, propertyName, out var value) && value != null)
                return ConvertObjectToString(value, defaultValue);
            return defaultValue;
        }

        private bool TryGetBoolProperty(object obj, string propertyName, bool defaultValue = false)
        {
            if (TryGetPropertyValue(obj, propertyName, out var value) && value != null)
            {
                var text = ConvertObjectToString(value, defaultValue ? "true" : "false");
                if (bool.TryParse(text, out var b))
                    return b;
            }
            return defaultValue;
        }


        #endregion





        private async Task<(string skillCode, string executeMode, object result)> ExecuteSkillCoreAsync(ExecSkillModel model)
        {
            if (model == null)
                throw new ArgumentException("请求体不能为空");

            string code = model.SkillCode?.Trim() ?? "";
            var args = model.Arguments ?? new Dictionary<string, object>();

            // 1. 临时 workflow
            if (model.Steps != null && model.Steps.Count > 0)
            {
                _logger.LogInformation("开始执行临时 workflow，SkillCode={SkillCode}", code);
                var result = await RunWorkflowAsync(model.Steps, args);
                return (code, "temp_workflow", result);
            }

            // 2. 数据库 workflow
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
                    throw new ArgumentException("SkillActions JSON 格式错误");
                }

                _logger.LogInformation("开始执行 workflow 技能，SkillCode={SkillCode}", code);
                var result = await RunWorkflowAsync(steps, args);
                return (code, "workflow", result);
            }

            // 3. builtin
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("SkillCode 不能为空，或者请直接传 Steps");

            _logger.LogInformation("开始执行 builtin 技能，SkillCode={SkillCode}", code);
            var builtinResult = await ExecuteSkillInternal(code, args);
            return (code, "builtin", builtinResult);
        }


        private BrowserPageState? ParseBrowserPageState(object pageObj)
        {
            var state = new BrowserPageState();
            if (TryGetPropertyValue(pageObj, "url", out var url)) state.Url = ConvertObjectToString(url);
            if (TryGetPropertyValue(pageObj, "title", out var title)) state.Title = ConvertObjectToString(title);
            return string.IsNullOrWhiteSpace(state.Url) && string.IsNullOrWhiteSpace(state.Title) ? null : state;
        }

        private BrowserSessionState? ParseBrowserSessionState(object sessionObj)
        {
            var state = new BrowserSessionState();
            if (TryGetPropertyValue(sessionObj, "sessionId", out var sessionId)) state.SessionId = ConvertObjectToString(sessionId);
            if (TryGetPropertyValue(sessionObj, "reusable", out var reusable)) state.Reusable = TryConvertBool(reusable);
            if (TryGetPropertyValue(sessionObj, "keepAliveSuggested", out var keepAlive)) state.KeepAliveSuggested = TryConvertBool(keepAlive);
            if (TryGetPropertyValue(sessionObj, "timeoutMinutes", out var timeout)) state.TimeoutMinutes = TryConvertInt(timeout);
            if (TryGetPropertyValue(sessionObj, "followUpHint", out var hint)) state.FollowUpHint = ConvertObjectToString(hint);
            return string.IsNullOrWhiteSpace(state.SessionId) && string.IsNullOrWhiteSpace(state.FollowUpHint) ? null : state;
        }



        #endregion


        #region 转换工具

        private List<string> ConvertToStringList(object value)
        {
            if (value == null) return new List<string>();
            if (value is List<string> list) return list;
            if (value is string s) return new List<string> { s };
            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                    return je.EnumerateArray().Select(x => x.ToString()).ToList();
                return new List<string> { je.ToString() };
            }
            if (value is System.Collections.IEnumerable enumerable)
            {
                var result = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item != null) result.Add(item.ToString() ?? "");
                }
                return result;
            }
            return new List<string> { value.ToString() ?? "" };
        }



        private bool TryConvertBool(object? value)
        {
            return bool.TryParse(ConvertObjectToString(value), out var b) && b;
        }

        private int TryConvertInt(object? value)
        {
            return int.TryParse(ConvertObjectToString(value), out var i) ? i : 0;
        }

        private bool TryGetPropertyValue(object obj, string propertyName, out object? value)
        {
            value = null;
            if (obj == null) return false;

            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    if (je.TryGetProperty(propertyName, out var child))
                    {
                        value = child;
                        return true;
                    }
                    // 这里重命名为 jsonProp
                    foreach (var jsonProp in je.EnumerateObject())
                    {
                        if (string.Equals(jsonProp.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                        {
                            value = jsonProp.Value;
                            return true;
                        }
                    }
                }
                return false;
            }

            // 这里保留 prop 或重命名为 property 都可以
            var prop = obj.GetType().GetProperty(propertyName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) return false;
            value = prop.GetValue(obj);
            return true;
        }

        private List<string> TryInferMissingArgs(string skillCode, Dictionary<string, object>? args, string errorMessage)
        {
            var safeArgs = args ?? new Dictionary<string, object>();
            var missing = new List<string>();
            var code = (skillCode ?? "").Trim().ToLowerInvariant();

            void Check(string key)
            {
                if (!safeArgs.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(ConvertObjectToString(value)))
                    missing.Add(key);
            }

            switch (code)
            {
                case "browser_task":
                    Check("actions");
                    break;
                case "open_task":
                case "print_task":
                    Check("path");
                    break;
                case "folder_task":
                    Check("source");
                    break;
                case "file_task":
                    {
                        var action = GetString(safeArgs, "action").Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(action))
                        {
                            missing.Add("action");
                            break;
                        }
                        switch (action)
                        {
                            case "search":
                                Check("keyword");
                                break;
                            case "copy":
                            case "move":
                                Check("from");
                                Check("to");
                                break;
                            case "rename":
                                Check("from");
                                Check("newName");
                                break;
                            case "mkdir":
                                Check("from");
                                break;
                        }
                        break;
                    }
                case "tool_task":
                    Check("exePath");
                    break;
                case "wechat_task":
                    {
                        var action = GetString(safeArgs, "action").Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(action))
                        {
                            missing.Add("action");
                            break;
                        }
                        switch (action)
                        {
                            case "text":
                            case "markdown":
                                Check("content");
                                break;
                            case "card":
                                Check("title");
                                Check("desc");
                                Check("url");
                                break;
                        }
                        break;
                    }
                case "email_task":
                    {
                        var action = GetString(safeArgs, "action").Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(action))
                        {
                            if (safeArgs.ContainsKey("to"))
                                action = "send";
                            else if (safeArgs.ContainsKey("subjectKeyword") || safeArgs.ContainsKey("fromKeyword") || safeArgs.ContainsKey("bodyKeyword"))
                                action = "search";
                            else
                                action = "search";
                        }

                        bool HasValue(string key)
                        {
                            return safeArgs.TryGetValue(key, out var value) &&
                                   !string.IsNullOrWhiteSpace(ConvertObjectToString(value));
                        }

                        bool hasMailTarget = HasValue("mailRef") || HasValue("index");

                        switch (action)
                        {
                            case "send":
                                Check("to");
                                break;

                            case "search":
                                break;

                            case "read":
                            case "mark_read":
                                if (!hasMailTarget)
                                    missing.Add("mailRef 或 index");
                                break;

                            case "download_attachments":
                                if (!hasMailTarget)
                                    missing.Add("mailRef 或 index");
                                Check("savePath");
                                break;

                            case "reply":
                                if (!hasMailTarget)
                                    missing.Add("mailRef 或 index");
                                if (!HasValue("replyText") && !HasValue("replyHtml"))
                                    missing.Add("replyText 或 replyHtml");
                                break;

                            case "save_eml":
                                if (!hasMailTarget)
                                    missing.Add("mailRef 或 index");
                                Check("filePath");
                                break;
                        }

                        break;
                    }


            }

            if (missing.Count == 0 && !string.IsNullOrWhiteSpace(errorMessage))
            {
                foreach (var key in new[] { "actions", "path", "source", "from", "to", "newName", "keyword", "exePath", "to", "content", "title", "desc", "url" })
                {
                    if (errorMessage.Contains(key, StringComparison.OrdinalIgnoreCase) && !missing.Contains(key))
                        missing.Add(key);
                }
            }

            return missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        #endregion





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

            try
            {
                var (skillCode, executeMode, result) = await ExecuteSkillCoreAsync(model);
                
                return Ok(ResponseHelper.Success(new
                {
                    skillCode,
                    executeMode,
                    result
                }));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "安全限制，Message={Message}", ex.Message);
                return StatusCode(403, ResponseHelper.Fail<object>(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "文件不存在，Message={Message}", ex.Message);
                return NotFound(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "不支持的技能，Message={Message}", ex.Message);
                return BadRequest(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "参数错误，Message={Message}", ex.Message);
                return BadRequest(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行技能异常");
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
        /// <summary>
        /// 执行 workflow 技能（瘦身版日志）
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
        /// - log: 每一步的简要执行日志
        /// </summary>
        /// <summary>
        /// 执行 workflow 技能（默认精简版返回）
        ///
        /// 说明：
        /// 1. 默认只返回必要信息：success / msg / totalSteps / completedSteps / lastResult
        /// 2. 只有当 input 中传了 debug=true 时，才返回详细 log
        /// 3. 每一步执行结果仍会写入上下文，供后续步骤引用
        /// </summary>
        private async Task<object> RunWorkflowAsync(List<SkillStep>? steps, Dictionary<string, object>? input)
        {
            var safeSteps = steps ?? new List<SkillStep>();
            var context = input != null
                ? new Dictionary<string, object>(input)
                : new Dictionary<string, object>();

            bool debug = false;
            if (input != null && input.TryGetValue("debug", out var debugObj))
            {
                var debugText = ConvertObjectToString(debugObj, "false");
                debug = bool.TryParse(debugText, out var dbg) && dbg;
            }

            var log = new List<object>();
            object? lastResult = null;

            if (safeSteps.Count == 0)
            {
                if (debug)
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

                return new
                {
                    success = true,
                    msg = "workflow 没有可执行步骤",
                    totalSteps = 0,
                    completedSteps = 0,
                    lastResult = (object?)null
                };
            }

            for (int i = 0; i < safeSteps.Count; i++)
            {
                var step = safeSteps[i];
                var action = step?.Action?.Trim() ?? "";
                var stepArgs = step?.Args ?? new Dictionary<string, object>();

                if (string.IsNullOrWhiteSpace(action))
                {
                    if (debug)
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

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = "",
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult
                    };
                }

                Dictionary<string, object> resolvedArgs;
                try
                {
                    resolvedArgs = ResolveTemplateVariables(stepArgs, context);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "步骤参数模板解析失败，StepIndex={StepIndex}，Action={Action}", i, action);

                    if (debug)
                    {
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

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = action,
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult
                    };
                }

                try
                {
                    _logger.LogInformation("开始执行 workflow 步骤，StepIndex={StepIndex}，Action={Action}", i, action);

                    var result = await ExecuteSkillInternal(action, resolvedArgs);

                    context[$"step{i}"] = result;
                    lastResult = result;

                    if (debug)
                    {
                        string resultType = "";
                        string resultText = "";

                        try
                        {
                            var resultJson = JsonSerializer.Serialize(result);
                            using var resultDoc = JsonDocument.Parse(resultJson);
                            var root = resultDoc.RootElement;

                            if (root.TryGetProperty("Type", out var typeEl))
                                resultType = typeEl.GetString() ?? "";

                            if (root.TryGetProperty("Text", out var textEl))
                                resultText = textEl.GetString() ?? "";
                        }
                        catch
                        {
                        }

                        log.Add(new
                        {
                            stepIndex = i,
                            step = action,
                            success = true,
                            args = resolvedArgs,
                            resultSummary = new
                            {
                                type = resultType,
                                text = resultText
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "workflow 步骤执行失败，StepIndex={StepIndex}，Action={Action}", i, action);

                    if (debug)
                    {
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

                    return new
                    {
                        success = false,
                        msg = "workflow 执行失败",
                        failedAt = i,
                        failedStep = action,
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult
                    };
                }
            }

            if (debug)
            {
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

            return new
            {
                success = true,
                msg = "workflow 执行完成",
                totalSteps = safeSteps.Count,
                completedSteps = safeSteps.Count,
                lastResult
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
                "email_task" => await DoEmailTaskAsync(safeArgs),

                "browser_task" => await DoBrowserTaskAsync(safeArgs),
                "wechat_task" => await DoWechatTaskAsync(safeArgs),
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
                // 先替换 {{ ... }}
                var replaced = Regex.Replace(rawText, @"\{\{\s*([^}]+?)\s*\}\}", match =>
                {
                    var expr = match.Groups[1].Value.Trim();
                    return ResolveTemplateExpression(expr, context);
                });

                // 再兼容 ${ ... }
                replaced = Regex.Replace(replaced, @"\$\{\s*([^}]+?)\s*\}", match =>
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
        /// 从对象中读取指定成员值（大小写不敏感）
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

            // 1. Dictionary<string, object>（大小写不敏感查找）
            if (obj is IDictionary<string, object> dict)
            {
                // 先直接取
                if (dict.TryGetValue(memberName, out var value))
                    return value;

                // 再做大小写不敏感匹配
                var matchedKey = dict.Keys.FirstOrDefault(k =>
                    string.Equals(k, memberName, StringComparison.OrdinalIgnoreCase));

                if (matchedKey != null && dict.TryGetValue(matchedKey, out var matchedValue))
                    return matchedValue;

                return null;
            }

            // 2. JsonElement（对象类型，属性名大小写不敏感）
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    // 先直接尝试
                    if (je.TryGetProperty(memberName, out var child))
                        return child;

                    // 再遍历做大小写不敏感匹配
                    foreach (var prop in je.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, memberName, StringComparison.OrdinalIgnoreCase))
                            return prop.Value;
                    }
                }

                return null;
            }

            // 3. 普通对象 / 匿名对象（属性名大小写不敏感）
            var propInfo = obj.GetType().GetProperty(
                memberName,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase);

            if (propInfo != null)
                return propInfo.GetValue(obj);

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

        #region 原子技能：邮件

        private async Task<object> DoEmailTaskAsync(Dictionary<string, object> args)
        {
            CleanupMailContextCache();

            string action = GetString(args, "action").Trim().ToLowerInvariant();
            string inputContextKey = GetString(args, "contextKey", "default");
            string contextKey = BuildScopedMailContextKey(inputContextKey);

            if (string.IsNullOrWhiteSpace(action))
            {
                if (args.ContainsKey("to"))
                    action = "send";
                else if (args.ContainsKey("subjectKeyword") || args.ContainsKey("fromKeyword") || args.ContainsKey("bodyKeyword"))
                    action = "search";
                else
                    action = "search";
            }

            switch (action)
            {
                case "send":
                    {
                        var attachments = GetStringList(args, "attachments");
                        if (args.TryGetValue("attachment", out var singleAttachmentObj))
                        {
                            var singleAttachment = ConvertObjectToString(singleAttachmentObj).Trim();
                            if (!string.IsNullOrWhiteSpace(singleAttachment) &&
                                !attachments.Contains(singleAttachment, StringComparer.OrdinalIgnoreCase))
                            {
                                attachments.Add(singleAttachment);
                            }
                        }

                        var insertImagePaths = GetStringList(args, "insertImagePaths");

                        var safeAttachments = new List<string>();
                        foreach (var file in attachments)
                        {
                            safeAttachments.Add(ValidatePath(file, mustExist: true));
                        }

                        var safeInsertImagePaths = new List<string>();
                        foreach (var file in insertImagePaths)
                        {
                            safeInsertImagePaths.Add(ValidatePath(file, mustExist: true));
                        }

                        var sendArgs = new Dictionary<string, object>(args, StringComparer.OrdinalIgnoreCase)
                        {
                            ["attachments"] = safeAttachments,
                            ["insertImagePaths"] = safeInsertImagePaths
                        };

                        var raw = await MailKitHelper.SendEmailAsync(sendArgs);

                        return new SkillResult
                        {
                            Success = TryGetBoolProperty(raw, "Success"),
                            SkillCode = "email_task",
                            Type = "send_email",
                            Text = TryGetStringProperty(raw, "Text", "邮件发送完成"),
                            ResultText = TryGetStringProperty(raw, "Text", "邮件发送完成"),
                            ResultValue = "",
                            Data = TryGetPropertyValue(raw, "Data", out var dataObj) ? dataObj : null,
                            Error = ""
                        }.Normalize();
                    }

                case "search":
                    {
                        string subjectKeyword = GetString(args, "subjectKeyword");
                        string fromKeyword = GetString(args, "fromKeyword");
                        string bodyKeyword = GetString(args, "bodyKeyword");
                        bool unreadOnly = GetBoolArg(args, "unreadOnly", false);
                        bool hasAttachments = GetBoolArg(args, "hasAttachments", false);
                        int maxCount = Math.Clamp(GetIntArg(args, "maxCount", 10), 1, 100);
                        int scanCount = Math.Clamp(GetIntArg(args, "scanCount", Math.Max(maxCount * 10, 100)), 1, 1000);

                        DateTime? dateFrom = null;
                        DateTime? dateTo = null;

                        string dateFromText = GetString(args, "dateFrom");
                        if (DateTime.TryParse(dateFromText, out var dtFrom))
                            dateFrom = dtFrom;

                        string dateToText = GetString(args, "dateTo");
                        if (DateTime.TryParse(dateToText, out var dtTo))
                            dateTo = dtTo;

                        int daysBack = GetIntArg(args, "daysBack", 0);
                        if (daysBack > 0 && !dateFrom.HasValue)
                            dateFrom = DateTime.Now.Date.AddDays(-daysBack);

                        var items = await MailKitHelper.SearchEmailsForAiAsync(
                            subjectKeyword,
                            fromKeyword,
                            bodyKeyword,
                            unreadOnly,
                            hasAttachments,
                            maxCount,
                            scanCount,
                            dateFrom,
                            dateTo);

                        _mailContext[contextKey] = new MailContextCacheItem
                        {
                            CreatedAt = DateTime.Now,
                            LastAccessAt = DateTime.Now,
                            Items = items
                        };

                        var resultList = items.Select(x =>
                            $"{x.Index}. {x.Subject} | {x.From} | {x.DateText} | {(x.HasAttachments ? "有附件" : "无附件")} | {(x.IsUnread ? "未读" : "已读")}"
                        ).ToList();

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "search_email",
                            Text = items.Count == 0 ? "未找到符合条件的邮件" : $"找到 {items.Count} 封邮件",
                            ResultText = items.Count == 0 ? "未找到符合条件的邮件" : $"找到 {items.Count} 封邮件",
                            ResultList = resultList,
                            ResultValue = items.FirstOrDefault()?.MailRef ?? "",
                            Data = new
                            {
                                action = "search",
                                count = items.Count,
                                contextKey = inputContextKey,
                                scopedContextKey = contextKey,
                                items
                            },
                            Error = ""
                        }.Normalize();
                    }

                case "read":
                    {
                        var uid = ResolveUid(args, contextKey);
                        var detail = await MailKitHelper.ReadEmailForAiAsync(uid);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "read_email",
                            Text = $"已读取邮件：{detail.Subject}",
                            ResultText = string.IsNullOrWhiteSpace(detail.TextPreview) ? "邮件无正文" : detail.TextPreview,
                            ResultValue = detail.MailRef,
                            Data = detail,
                            Error = ""
                        }.Normalize();
                    }

                case "download_attachments":
                    {
                        var uid = ResolveUid(args, contextKey);
                        string savePath = GetString(args, "savePath");
                        if (string.IsNullOrWhiteSpace(savePath))
                            throw new ArgumentException("savePath 不能为空");

                        var fullSavePath = ValidatePath(savePath, mustExist: false);
                        var files = await MailKitHelper.DownloadAttachmentsAsync(uid, fullSavePath);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "download_attachments",
                            Text = files.Count == 0 ? "该邮件没有附件" : $"已下载 {files.Count} 个附件",
                            ResultText = files.Count == 0 ? "该邮件没有附件" : $"已下载 {files.Count} 个附件",
                            ResultList = files,
                            ResultValue = files.FirstOrDefault() ?? "",
                            Data = new
                            {
                                action = "download_attachments",
                                savePath = fullSavePath,
                                files,
                                count = files.Count
                            },
                            Error = ""
                        }.Normalize();
                    }

                case "mark_read":
                    {
                        var uid = ResolveUid(args, contextKey);
                        await MailKitHelper.MarkAsReadAsync(uid);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "mark_read",
                            Text = "已标记为已读",
                            ResultText = "已标记为已读",
                            ResultValue = "",
                            Data = new { action = "mark_read" },
                            Error = ""
                        }.Normalize();
                    }

                case "reply":
                    {
                        var uid = ResolveUid(args, contextKey);

                        string replyText = GetString(args, "replyText");
                        string replyHtml = GetString(args, "replyHtml");
                        bool replyToAll = GetBoolArg(args, "replyToAll", false);

                        if (string.IsNullOrWhiteSpace(replyText) && string.IsNullOrWhiteSpace(replyHtml))
                            throw new ArgumentException("replyText 或 replyHtml 至少提供一个");

                        var attachments = GetStringList(args, "attachments");
                        var safeAttachments = new List<string>();
                        foreach (var file in attachments)
                        {
                            safeAttachments.Add(ValidatePath(file, mustExist: true));
                        }

                        await MailKitHelper.ReplyToEmailAsync(
                            uid,
                            replyText,
                            string.IsNullOrWhiteSpace(replyHtml) ? null : replyHtml,
                            replyToAll,
                            safeAttachments);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "reply_email",
                            Text = "回复已发送",
                            ResultText = "回复已发送",
                            ResultValue = "",
                            Data = new
                            {
                                action = "reply",
                                replyToAll,
                                attachments = safeAttachments
                            },
                            Error = ""
                        }.Normalize();
                    }

                case "save_eml":
                    {
                        var uid = ResolveUid(args, contextKey);
                        string filePath = GetString(args, "filePath");
                        if (string.IsNullOrWhiteSpace(filePath))
                            throw new ArgumentException("filePath 不能为空");

                        var fullPath = ValidatePath(filePath, mustExist: false);
                        var dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrWhiteSpace(dir))
                            Directory.CreateDirectory(dir);

                        await MailKitHelper.SaveToEmlAsync(uid, fullPath);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "save_eml",
                            Text = "邮件已保存为 eml",
                            ResultText = "邮件已保存为 eml",
                            ResultValue = fullPath,
                            Data = new
                            {
                                action = "save_eml",
                                filePath = fullPath
                            },
                            Error = ""
                        }.Normalize();
                    }

                default:
                    throw new NotSupportedException($"email_task 不支持的操作：{action}");
            }
        }

        private UniqueId ResolveUid(Dictionary<string, object> args, string scopedContextKey)
        {
            string mailRef = GetString(args, "mailRef");
            if (!string.IsNullOrWhiteSpace(mailRef))
            {
                if (MailKitHelper.TryParseMailRef(mailRef, out var uidByRef))
                    return uidByRef;

                throw new ArgumentException("mailRef 格式不正确");
            }

            int index = GetIntArg(args, "index", 0);
            if (index <= 0)
                throw new ArgumentException("必须提供 mailRef 或 index");

            if (!_mailContext.TryGetValue(scopedContextKey, out var cache) || cache.Items.Count == 0)
                throw new ArgumentException("未找到邮件上下文，请先执行 search");

            cache.LastAccessAt = DateTime.Now;

            var item = cache.Items.FirstOrDefault(x => x.Index == index);
            if (item == null)
                throw new ArgumentException($"上下文中不存在第 {index} 封邮件");

            if (!MailKitHelper.TryParseMailRef(item.MailRef, out var uid))
                throw new ArgumentException("缓存中的 mailRef 无效");

            return uid;
        }

        private void CleanupMailContextCache()
        {
            var expireBefore = DateTime.Now.AddMinutes(-MailContextExpireMinutes);
            foreach (var kv in _mailContext)
            {
                if (kv.Value.LastAccessAt < expireBefore)
                {
                    _mailContext.TryRemove(kv.Key, out _);
                }
            }
        }

        private string BuildScopedMailContextKey(string inputContextKey)
        {
            string userKey = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(userKey))
                userKey = "anonymous";

            return $"{userKey}:{inputContextKey}";
        }

        private bool GetBoolArg(Dictionary<string, object>? args, string key, bool defaultValue = false)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            if (value is bool b) return b;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
                if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var bj))
                    return bj;
            }

            return bool.TryParse(ConvertObjectToString(value), out var parsed) ? parsed : defaultValue;
        }

        private int GetIntArg(Dictionary<string, object>? args, string key, int defaultValue = 0)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            if (value is int i) return i;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var ji))
                    return ji;
                if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var js))
                    return js;
            }

            return int.TryParse(ConvertObjectToString(value), out var parsed) ? parsed : defaultValue;
        }

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
                "copy_many" => await CopyManyAsync(args),
                "move_many" => await MoveManyAsync(args),
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


        #region 原子技能：企业微信

        /// <summary>
        /// 企业微信机器人消息发送
        ///
        /// action:
        /// - text
        /// - markdown
        /// - card
        /// </summary>
        private async Task<object> DoWechatTaskAsync(Dictionary<string, object> args)
        {
            string action = GetString(args, "action").Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentException("wechat_task 的 action 不能为空");

            switch (action)
            {
                case "text":
                    {
                        string content = GetString(args, "content");
                        if (string.IsNullOrWhiteSpace(content))
                            throw new ArgumentException("text 模式下 content 不能为空");

                        bool isAtAll = bool.TryParse(GetString(args, "isAtAll", "false"), out var b) && b;

                        string atUsersRaw = GetString(args, "atUsers");
                        string[] atUsers = string.IsNullOrWhiteSpace(atUsersRaw)
                            ? Array.Empty<string>()
                            : atUsersRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        string result = await WechatBotHelper.SendText(content, isAtAll, atUsers);

                        return new SkillResult
                        {
                            Success = result.StartsWith("成功"),
                            SkillCode = "wechat_task",
                            Type = "text",
                            Text = result,
                            Data = new
                            {
                                action = "text",
                                content,
                                isAtAll,
                                atUsers
                            },
                            Error = result.StartsWith("成功") ? "" : result
                        }.Normalize();
                    }

                case "markdown":
                    {
                        string content = GetString(args, "content");
                        if (string.IsNullOrWhiteSpace(content))
                            throw new ArgumentException("markdown 模式下 content 不能为空");

                        string result = await WechatBotHelper.SendMarkdown(content);

                        return new SkillResult
                        {
                            Success = result.StartsWith("成功"),
                            SkillCode = "wechat_task",
                            Type = "markdown",
                            Text = result,
                            Data = new
                            {
                                action = "markdown",
                                content
                            },
                            Error = result.StartsWith("成功") ? "" : result
                        }.Normalize();
                    }

                case "card":
                    {
                        string title = GetString(args, "title");
                        string desc = GetString(args, "desc");
                        string url = GetString(args, "url");
                        string picUrl = GetString(args, "picUrl");

                        if (string.IsNullOrWhiteSpace(title))
                            throw new ArgumentException("card 模式下 title 不能为空");
                        if (string.IsNullOrWhiteSpace(desc))
                            throw new ArgumentException("card 模式下 desc 不能为空");
                        if (string.IsNullOrWhiteSpace(url))
                            throw new ArgumentException("card 模式下 url 不能为空");

                        string result = await WechatBotHelper.SendCard(title, desc, url, picUrl);

                        return new SkillResult
                        {
                            Success = result.StartsWith("成功"),
                            SkillCode = "wechat_task",
                            Type = "card",
                            Text = result,
                            Data = new
                            {
                                action = "card",
                                title,
                                desc,
                                url,
                                picUrl
                            },
                            Error = result.StartsWith("成功") ? "" : result
                        }.Normalize();
                    }

                default:
                    throw new NotSupportedException($"wechat_task 不支持的操作：{action}");
            }
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
        /// 3. 支持 actions 以 JSON 字符串或 JsonElement 数组传入
        /// 4. 支持小写字段名：type / url / selector / value
        /// </summary>
        /// <summary>
        /// 浏览器技能（瘦身版返回）
        ///
        /// 参数：
        /// {
        ///   "actions": [ ...BrowserAction数组... ],
        ///   "sessionId": "可选",
        ///   "closeSession": false,
        ///   "includeOutputs": false
        /// }
        ///
        /// 说明：
        /// 1. 支持 actions 以 JSON 字符串或 JsonElement 数组传入
        /// 2. 支持小写字段名：type / url / selector / value
        /// 3. 默认不返回完整 outputs，避免结果过大
        /// 4. 最终只返回：sessionId、page、list、result
        /// </summary>
        /// <summary>
        /// 浏览器技能（默认精简版返回）
        ///
        /// 参数：
        /// {
        ///   "actions": [ ...BrowserAction数组... ],
        ///   "sessionId": "可选",
        ///   "closeSession": false,
        ///   "includeOutputs": false
        /// }
        ///
        /// 说明：
        /// 1. 默认不返回完整 outputs，避免结果过大
        /// 2. 最终只返回：sessionId、page、count、list、result
        /// 3. 当 includeOutputs=true 时，才返回每一步动作明细
        /// </summary>
        private async Task<object> DoBrowserTaskAsync(Dictionary<string, object> args)
        {
            List<BrowserAction> actions;

            if (!args.TryGetValue("actions", out var actionsObj) || actionsObj == null)
                throw new ArgumentException("browser_task 必须提供 actions");

            try
            {
                if (actionsObj is string actionsJson)
                {
                    actions = JsonSerializer.Deserialize<List<BrowserAction>>(actionsJson, BrowserJsonOptions)
                              ?? new List<BrowserAction>();
                }
                else if (actionsObj is JsonElement je)
                {
                    actions = JsonSerializer.Deserialize<List<BrowserAction>>(je.GetRawText(), BrowserJsonOptions)
                              ?? new List<BrowserAction>();
                }
                else
                {
                    throw new ArgumentException("actions 格式不正确，必须是 JSON 数组");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "browser_task 的 actions JSON 解析失败");
                throw new ArgumentException("actions JSON 解析失败：" + ex.Message);
            }

            if (actions.Count == 0)
                throw new ArgumentException("browser_task 的 actions 不能为空");

            for (int i = 0; i < actions.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(actions[i].Type))
                    throw new ArgumentException($"第 {i + 1} 个 action 的 type 不能为空");
            }

            string sessionId = GetString(args, "sessionId");
            bool closeSession = bool.TryParse(GetString(args, "closeSession", "false"), out var close) && close;
            bool includeOutputs = bool.TryParse(GetString(args, "includeOutputs", "false"), out var io) && io;

            BrowserSession? session = null;

            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                session = _browserService.GetSession(sessionId);
            }

            if (session == null)
            {
                session = await _browserService.CreateSessionAsync();
            }

            var outputs = new List<BrowserActionResult>();

            try
            {
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

                var final = _browserService.BuildFinalResult(outputs);

                // 统一补一个 count，方便 workflow 和 AI 直接使用
                int count = final.FinalList?.Count ?? 0;

                object data = includeOutputs
                    ? new
                    {
                        sessionId = session.SessionId,
                        page = new
                        {
                            url = session.CurrentPage.Url,
                            title = await _browserService.SafeGetTitleAsync(session.CurrentPage)
                        },
                        count,
                        list = final.FinalList,
                        result = final.FinalData,
                        outputs
                    }
                    : new
                    {
                        sessionId = session.SessionId,
                        page = new
                        {
                            url = session.CurrentPage.Url,
                            title = await _browserService.SafeGetTitleAsync(session.CurrentPage)
                        },
                        count,
                        list = final.FinalList,
                        result = final.FinalData
                    };

                return new SkillResult
                {
                    Success = true,
                    SkillCode = "browser_task",
                    Type = final.FinalType,
                    Text = final.FinalText,
                    Data = data
                };
            }
            finally
            {
                if (closeSession && session != null)
                {
                    await _browserService.CloseSession(session.SessionId);
                }
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
            timeoutSec = Math.Clamp(timeoutSec, 1, 120);

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
                }

                throw new TimeoutException($"外部工具执行超时（{timeoutSec}秒）");
            }

            string output = await outputTask;
            string error = await errorTask;

            return new SkillResult
            {
                Success = process.ExitCode == 0,
                SkillCode = "tool_task",
                Type = "run_exe",
                Text = process.ExitCode == 0 ? "工具执行完成" : "工具执行失败",
                Data = new
                {
                    exePath = fullExePath,
                    arguments,
                    exitCode = process.ExitCode,
                    output = string.IsNullOrWhiteSpace(output) ? "" : output,
                    error = string.IsNullOrWhiteSpace(error) ? "" : error
                },
                Error = process.ExitCode == 0 ? "" : error
            }.Normalize();
        }


        #endregion

        #region 文件搜索

        /// <summary>
        /// 搜索文件：
        /// 优先 Everything → 降级 Windows Search → 最后递归搜索
        /// </summary>
        private async Task<SkillResult> SearchFileAsync(string keyword, string ext = "*")
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("搜索关键词不能为空");

            List<string> resultList = new();

            try
            {
                resultList = await SearchWithEverythingAsync(keyword, ext);

                if (!resultList.Any())
                    resultList = await SearchWithWindowsSearchAsync(keyword, ext);
            }
            catch
            {
                resultList = await SearchFallbackAsync(keyword, ext);
            }

            // 去重
            resultList = resultList
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (resultList.Count == 0)
            {
                return new SkillResult
                {
                    Success = false,
                    SkillCode = "file_task",
                    Type = "search",
                    Text = "未找到文件",
                    ResultText = "未找到文件",
                    ResultList = new List<string>(),
                    ResultValue = "",
                    Data = new
                    {
                        keyword,
                        ext,
                        firstPath = "",
                        paths = Array.Empty<string>(),
                        count = 0
                    },
                    Error = "未找到文件"
                }.Normalize();
            }

            var firstPath = resultList.FirstOrDefault() ?? "";

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "search",
                Text = $"找到 {resultList.Count} 个文件",
                ResultText = $"找到 {resultList.Count} 个文件",
                ResultList = resultList,
                ResultValue = firstPath,
                Data = new
                {
                    keyword,
                    ext,
                    firstPath,
                    paths = resultList,
                    count = resultList.Count
                }
            }.WithList(resultList);
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
                    // 注意：很多 Everything SDK 中：
                    // item.Path = 目录
                    // item.Name = 文件名
                    string dir = item.Path ?? "";
                    string name = item.Name ?? "";

                    string fullPath = string.IsNullOrWhiteSpace(name)
                        ? dir
                        : Path.Combine(dir, name);

                    if (string.IsNullOrWhiteSpace(fullPath))
                        continue;

                    // 只保留真实文件，不要目录
                    if (!System.IO.File.Exists(fullPath))
                        continue;

                    // 过滤快捷方式，避免返回 Recent 里的 .lnk
                    if (fullPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrWhiteSpace(ext) &&
                        ext != "*" &&
                        !fullPath.EndsWith($".{ext.Trim('.')}", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    ValidatePath(fullPath, mustExist: true);
                    list.Add(fullPath);
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
                            // 过滤快捷方式
                            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                continue;

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
            }.Normalize();
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
            }.Normalize();
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
            }.Normalize();
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
            }.WithValue(fullPath);
        }

        #region 批量文件操作
        private async Task<SkillResult> CopyManyAsync(Dictionary<string, object> args)
        {
            var paths = GetStringList(args, "paths");
            string toDir = GetString(args, "toDir");
            bool overwrite = bool.TryParse(GetString(args, "overwrite", "true"), out var ov) && ov;

            if (paths.Count == 0)
                throw new ArgumentException("paths 不能为空");

            if (string.IsNullOrWhiteSpace(toDir))
                throw new ArgumentException("toDir 不能为空");

            var fullTargetDir = ValidatePath(toDir, mustExist: false);

            int successCount = 0;
            var copied = new List<string>();
            var failed = new List<object>();

            await Task.Run(() =>
            {
                Directory.CreateDirectory(fullTargetDir);

                foreach (var item in paths)
                {
                    try
                    {
                        var source = ValidatePath(item, mustExist: true);
                        if (!System.IO.File.Exists(source))
                            throw new FileNotFoundException($"文件不存在: {source}");

                        var target = Path.Combine(fullTargetDir, Path.GetFileName(source));
                        var validatedTarget = ValidatePath(target, mustExist: false);

                        System.IO.File.Copy(source, validatedTarget, overwrite);
                        copied.Add(validatedTarget);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new
                        {
                            path = item,
                            error = ex.Message
                        });
                    }
                }
            });

            return new SkillResult
            {
                Success = successCount > 0,
                SkillCode = "file_task",
                Type = "copy_many",
                Text = successCount > 0
                    ? $"批量复制完成，成功 {successCount} 个，失败 {failed.Count} 个"
                    : "批量复制失败",
                Data = new
                {
                    toDir = fullTargetDir,
                    successCount,
                    failedCount = failed.Count,
                    copied,
                    failed
                },
                Error = successCount > 0 ? "" : "没有文件复制成功"
            }.Normalize();
        }

        private async Task<SkillResult> MoveManyAsync(Dictionary<string, object> args)
        {
            var paths = GetStringList(args, "paths");
            string toDir = GetString(args, "toDir");
            bool overwrite = bool.TryParse(GetString(args, "overwrite", "true"), out var ov) && ov;

            if (paths.Count == 0)
                throw new ArgumentException("paths 不能为空");

            if (string.IsNullOrWhiteSpace(toDir))
                throw new ArgumentException("toDir 不能为空");

            var fullTargetDir = ValidatePath(toDir, mustExist: false);

            int successCount = 0;
            var moved = new List<string>();
            var failed = new List<object>();

            await Task.Run(() =>
            {
                Directory.CreateDirectory(fullTargetDir);

                foreach (var item in paths)
                {
                    try
                    {
                        var source = ValidatePath(item, mustExist: true);
                        if (!System.IO.File.Exists(source))
                            throw new FileNotFoundException($"文件不存在: {source}");

                        var target = Path.Combine(fullTargetDir, Path.GetFileName(source));
                        var validatedTarget = ValidatePath(target, mustExist: false);

                        if (System.IO.File.Exists(validatedTarget))
                        {
                            if (overwrite)
                                System.IO.File.Delete(validatedTarget);
                            else
                                throw new IOException($"目标文件已存在: {validatedTarget}");
                        }

                        System.IO.File.Move(source, validatedTarget);
                        moved.Add(validatedTarget);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new
                        {
                            path = item,
                            error = ex.Message
                        });
                    }
                }
            });

            return new SkillResult
            {
                Success = successCount > 0,
                SkillCode = "file_task",
                Type = "move_many",
                Text = successCount > 0
                    ? $"批量移动完成，成功 {successCount} 个，失败 {failed.Count} 个"
                    : "批量移动失败",
                Data = new
                {
                    toDir = fullTargetDir,
                    successCount,
                    failedCount = failed.Count,
                    moved,
                    failed
                },
                Error = successCount > 0 ? "" : "没有文件移动成功"
            }.Normalize();
        }

        #endregion


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

        private List<string> GetStringList(Dictionary<string, object>? args, string key)
        {
            var result = new List<string>();

            if (args == null || !args.TryGetValue(key, out var value) || value == null)
                return result;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in je.EnumerateArray())
                    {
                        var text = ConvertObjectToString(item).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                            result.Add(text);
                    }
                    return result;
                }

                if (je.ValueKind == JsonValueKind.String)
                {
                    var text = je.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                    return result;
                }
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                {
                    var text = ConvertObjectToString(item).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                }
                return result;
            }

            var single = ConvertObjectToString(value).Trim();
            if (!string.IsNullOrWhiteSpace(single))
                result.Add(single);

            return result;
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
        public async Task<IActionResult> DeleteSkill([FromBody] SkillBaseModel model)
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


    public class SkillBaseModel
    {
        public int ID { get; set; }

        public string SkillCode { get; set; } = "";

        public string Remark { get; set; } = "";
        
    }


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
