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
    /// Skill controller
    ///
    /// Design goals:
    /// 1. Support independent execution of atomic skills (files, printing, screenshots, email, browser, etc.)
    /// 2. Support composite skills (workflows) defined in the database
    /// 3. Pass values between steps through template variables, for example:
    ///    {{step0}}
    ///    {{step0.path}}
    ///    {{step0.data.path}}
    /// 4. Keep return values as stable as possible for AI callers
    /// </summary>
    //[Authorize(AuthenticationSchemes = "ApiKey")] // Enable this in production to require API key authentication for external calls.
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : BaseCommandController
    {
        private readonly ILogger<SkillsController> _logger;
        private readonly BrowserService _browserService;
        private readonly FileSystemOptions _fsOptions;


        // Shared cache used by both endpoints
        private static string? _cachedManifestJson;
        private static JsonDocument? _cachedManifestDoc;

        // Cache lock (prevents duplicate concurrent loads)
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
        /// Context cache for email search results
        /// Supports follow-up references such as "the first email" and "the previous email"
        /// Use contextKey as the key
        /// </summary>
        private static readonly ConcurrentDictionary<string, List<EmailListItemDto>> _mailContextCache = new(StringComparer.OrdinalIgnoreCase);


        /// <summary>
        /// Allowlist of external executables
        /// Executable files must be configured in the configuration file
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



        #region Internal Processing
        /// <summary>
        /// JSON options used when browser_task deserializes browser actions
        /// Notes:
        /// 1. Allow lowercase fields to map to BrowserAction properties
        /// 2. Allow numeric fields to be supplied as strings, for example "take": "10"
        /// </summary>
        private static readonly JsonSerializerOptions BrowserJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };

        #endregion

        #region AI Skill Features        

        /// <summary>
        /// Return a skill overview to the AI:
        /// 1. workflows = Composite skills stored in the database (prefer direct invocation)
        /// 2. builtins = Builtin atomic skills defined in skill-manifest.json (combine them when no ready-made skill exists)
        ///
        /// Recommended AI usage strategy:
        /// - First check whether workflows contains a ready-made skill
        /// - If not, inspect builtins and call GetBuiltinSkillManifest
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
                    _logger.LogError(ex, "Failed to read the builtin skill catalog; builtins will return an empty array");
                }

                return Ok(ResponseHelper.Success(new
                {
                    workflows,
                    builtins
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve the AI skill list");
                return StatusCode(500, ResponseHelper.Fail<object>("Failed to retrieve the skill list"));
            }
        }



        #region GetBuiltinSkillDetail - Retrieve Builtin Definition Details
        [HttpPost("GetBuiltinSkillDetail")]
        public IActionResult GetBuiltinSkillDetail([FromBody] SkillBaseModel request)
        {
            if (string.IsNullOrWhiteSpace(request.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode cannot be empty"));

            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("Builtin skill manifest file not found: {FilePath}", filePath);
                    return NotFound(ResponseHelper.Fail<object>("skill-manifest.json does not exist"));
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
                    return NotFound(ResponseHelper.Fail<object>("No builtins were found in the manifest"));
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

                return NotFound(ResponseHelper.Fail<object>("The builtin skill was not found"));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json has an invalid format");
                return StatusCode(500, ResponseHelper.Fail<object>("skill-manifest.json has an invalid format"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read builtin skill details. SkillCode={SkillCode}", request.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("Failed to read builtin skill details"));
            }
        }

        #endregion


        #region Reserved for Future Use
        [HttpPost("GetSkillListWithBuiltinForAI")]
        public async Task<IActionResult> GetSkillListWithBuiltinForAI()
        {
            try
            {
                // 1. Database skills
                var sql = "SELECT SkillCode, Remark AS AIDesc FROM Skills ORDER BY ID ASC";
                var workflows = (await QueryAsync<dynamic>(sql)).ToList();

                // 2. Builtin atomic skills (reuse the cache)
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
                            // Clone it to prevent external modification
                            builtins = _cachedManifestDoc.RootElement.Clone();
                        }
                    }
                    else
                    {
                        _logger.LogWarning("skill-manifest.json does not exist; builtins will return an empty object");
                        builtins = JsonDocument.Parse("{}").RootElement.Clone();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to read skill-manifest.json; builtins will return an empty object");
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
                _logger.LogError(ex, "Failed to retrieve the AI skill list");
                return StatusCode(500, ResponseHelper.Fail<object>("Failed to retrieve the skill list"));
            }
        }
        #endregion

        /// <summary>
        /// Return the manifest for system builtin atomic skills
        /// </summary>        
        [HttpPost("GetBuiltinSkillManifest")]
        public IActionResult GetBuiltinSkillManifest()
        {
            try
            {
                var filePath = Path.Combine(AppContext.BaseDirectory, "AiConfig", "skill-manifest.json");
                if (!System.IO.File.Exists(filePath))
                {
                    _logger.LogWarning("Builtin skill manifest file not found: {FilePath}", filePath);
                    return NotFound(ResponseHelper.Fail<object>("skill-manifest.json does not exist"));
                }

                // Global cache: load only once (use a byte array to handle the BOM automatically)
                lock (_cacheLock)
                {
                    if (_cachedManifestDoc == null)
                    {
                        byte[] jsonBytes = System.IO.File.ReadAllBytes(filePath);
                        _cachedManifestDoc = JsonDocument.Parse(jsonBytes);
                        _cachedManifestJson = null; // The string cache is no longer used
                    }
                }

                var data = _cachedManifestDoc.RootElement.Clone();
                return Ok(ResponseHelper.Success(data));
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "skill-manifest.json has an invalid format at Line {Line}, Pos {Pos}", ex.LineNumber, ex.BytePositionInLine);
                return StatusCode(500, ResponseHelper.Fail<object>("skill-manifest.json has an invalid format"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read the builtin skill manifest");
                return StatusCode(500, ResponseHelper.Fail<object>("Failed to read the builtin skill manifest"));
            }
        }



        /// <summary>
        /// Retrieve the detailed definition of a workflow skill
        ///
        /// Intended for AI usage:
        /// 1. Call GetSkillListForAI first to see which workflows are available
        /// 2. Then use this endpoint to read the specific steps of a workflow
        /// 3. Return an explicit failure if skillCode does not exist
        /// 4. Return an explicit failure if SkillActions JSON has an invalid format
        /// </summary>        
        [HttpPost("GetSkillAction")]
        public async Task<IActionResult> GetSkillAction([FromBody] SkillBaseModel request)
        {
            if (string.IsNullOrWhiteSpace(request.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode cannot be empty"));

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
                    _logger.LogWarning("Skill definition not found: {SkillCode}", request.SkillCode);
                    return NotFound(ResponseHelper.Fail<object>("The skill was not found"));
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
                    _logger.LogError(ex, "Skill action JSON has an invalid format. SkillCode={SkillCode}", request.SkillCode);
                    return StatusCode(500, ResponseHelper.Fail<object>("SkillActions JSON has an invalid format"));
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
                // 🔥 Fixed here to use request.SkillCode
                _logger.LogError(ex, "Failed to retrieve skill details. SkillCode={SkillCode}", request.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("Failed to retrieve skill details"));
            }
        }


        #endregion


        #region Execution Endpoints

        #region Coze Compatibility

        #region Coze Compatibility

        [HttpPost("ExecuteSkillForCoze")]
        public async Task<IActionResult> ExecuteSkillForCoze([FromBody] CozeSimpleRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Json))
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "Request JSON is missing",
                    skillCode: "",
                    executeMode: "unknown",
                    errorCode: "INVALID_REQUEST",
                    errorMessage: "Json cannot be empty",
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
                _logger.LogError(ex, "Failed to parse Coze JSON");
                return Ok(CozeSkillResponse.Fail(
                    message: "Failed to parse the request JSON",
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
                    message: needMoreInput ? "Required arguments are missing" : "Invalid arguments",
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
                    message: "You do not have permission to execute this skill",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "FORBIDDEN",
                    errorMessage: ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "The target file does not exist",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "FILE_NOT_FOUND",
                    errorMessage: ex.Message));
            }
            catch (NotSupportedException ex)
            {
                return Ok(CozeSkillResponse.Fail(
                    message: "Unsupported skill or operation",
                    skillCode: model?.SkillCode ?? "",
                    executeMode: "builtin",
                    errorCode: "NOT_SUPPORTED",
                    errorMessage: ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExecuteSkillForCoze execution failed");
                return Ok(CozeSkillResponse.Fail(
                    message: "Internal server error",
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
            // 1. Builtin result: map directly from SkillResult
            if (rawResult is SkillResult skill)
            {
                var response = new CozeSkillResponse
                {
                    Success = skill.Success,
                    Message = skill.Success ? "Execution succeeded" : (string.IsNullOrWhiteSpace(skill.Error) ? "Execution failed" : skill.Error),
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

                // Try to populate session/page
                FillCozeExtraFields(response, skill.Data);
                return response;
            }

            // 2. Workflow/temp_workflow result: flatten from lastResult
            var responseWorkflow = new CozeSkillResponse
            {
                Success = TryGetBoolProperty(rawResult, "success"),
                Message = TryGetStringProperty(rawResult, "msg", "Execution completed"),
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
                throw new ArgumentException("The request body cannot be empty");

            string code = model.SkillCode?.Trim() ?? "";
            var args = model.Arguments ?? new Dictionary<string, object>();

            // 1. Temporary workflow
            if (model.Steps != null && model.Steps.Count > 0)
            {
                _logger.LogInformation("Starting temporary workflow execution. SkillCode={SkillCode}", code);
                var result = await RunWorkflowAsync(model.Steps, args);
                return (code, "temp_workflow", result);
            }

            // 2. Database workflow
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
                    _logger.LogError(ex, "SkillActions JSON has an invalid format. SkillCode={SkillCode}", code);
                    throw new ArgumentException("SkillActions JSON has an invalid format");
                }

                _logger.LogInformation("Starting workflow skill execution. SkillCode={SkillCode}", code);
                var result = await RunWorkflowAsync(steps, args);
                return (code, "workflow", result);
            }

            // 3. builtin
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("SkillCode cannot be empty; alternatively, provide Steps directly");

            _logger.LogInformation("Starting builtin skill execution. SkillCode={SkillCode}", code);
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


        #region Conversion Utilities

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
                    // Rename it to jsonProp here
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

            // Either keep prop here or rename it to property
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
                                    missing.Add("mailRef or index");
                                break;

                            case "download_attachments":
                                if (!hasMailTarget)
                                    missing.Add("mailRef or index");
                                Check("savePath");
                                break;

                            case "reply":
                                if (!hasMailTarget)
                                    missing.Add("mailRef or index");
                                if (!HasValue("replyText") && !HasValue("replyHtml"))
                                    missing.Add("replyText or replyHtml");
                                break;

                            case "save_eml":
                                if (!hasMailTarget)
                                    missing.Add("mailRef or index");
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
        /// Unified execution endpoint
        ///
        /// Execution order:
        /// 1. If the request includes Steps directly, execute them as a temporary workflow
        /// 2. Otherwise, check the database for a workflow skill with the same name
        /// 3. If found, execute it as a database workflow
        /// 4. If not found, execute it as a builtin atomic skill
        /// </summary>
        [HttpPost("ExecuteSkill")]
        public async Task<IActionResult> ExecuteSkill([FromBody] ExecSkillModel model)
        {
            if (model == null)
                return BadRequest(ResponseHelper.Fail<object>("The request body cannot be empty"));

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
                _logger.LogWarning(ex, "Security restriction. Message={Message}", ex.Message);
                return StatusCode(403, ResponseHelper.Fail<object>(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "File not found. Message={Message}", ex.Message);
                return NotFound(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (NotSupportedException ex)
            {
                _logger.LogWarning(ex, "Unsupported skill. Message={Message}", ex.Message);
                return BadRequest(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid arguments. Message={Message}", ex.Message);
                return BadRequest(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while executing the skill");
                return StatusCode(500, ResponseHelper.Fail<object>("Internal server error"));
            }
        }




        #endregion

        #region Workflow Engine

        /// <summary>
        /// Execute a workflow skill
        ///
        /// Features:
        /// 1. Execute each step in order
        /// 2. Support template variables, for example:
        ///    {{step0}}
        ///    {{step0.path}}
        ///    {{step0.data.path}}
        /// 3. Write each step result to the context for later steps
        ///
        /// Returns:
        /// - success: Whether execution succeeded
        /// - msg: Execution result description
        /// - totalSteps: Total number of steps
        /// - completedSteps: Number of completed steps
        /// - failedAt: Index of the failed step (returned on failure)
        /// - failedStep: Name of the failed step (returned on failure)
        /// - lastResult: Result of the last successful step
        /// - log: Execution log for each step
        /// </summary>
        /// <summary>
        /// Execute a workflow skill (compact log)
        ///
        /// Features:
        /// 1. Execute each step in order
        /// 2. Support template variables, for example:
        ///    {{step0}}
        ///    {{step0.path}}
        ///    {{step0.data.path}}
        /// 3. Write each step result to the context for later steps
        ///
        /// Returns:
        /// - success: Whether execution succeeded
        /// - msg: Execution result description
        /// - totalSteps: Total number of steps
        /// - completedSteps: Number of completed steps
        /// - failedAt: Index of the failed step (returned on failure)
        /// - failedStep: Name of the failed step (returned on failure)
        /// - lastResult: Result of the last successful step
        /// - log: Brief execution log for each step
        /// </summary>
        /// <summary>
        /// Execute a workflow skill (compact response by default)
        ///
        /// Notes:
        /// 1. Return only essential information by default: success/msg/totalSteps/completedSteps/lastResult
        /// 2. Return the detailed log only when input contains debug=true
        /// 3. Continue writing each step result to the context for later steps
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
                        msg = "The workflow has no executable steps",
                        totalSteps = 0,
                        completedSteps = 0,
                        lastResult = (object?)null,
                        log
                    };
                }

                return new
                {
                    success = true,
                    msg = "The workflow has no executable steps",
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
                            error = "The step Action cannot be empty"
                        });

                        return new
                        {
                            success = false,
                            msg = "Workflow execution failed",
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
                        msg = "Workflow execution failed",
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
                    _logger.LogError(ex, "Failed to resolve the step argument template. StepIndex={StepIndex}, Action={Action}", i, action);

                    if (debug)
                    {
                        log.Add(new
                        {
                            stepIndex = i,
                            step = action,
                            success = false,
                            error = "Failed to resolve the argument template: " + ex.Message
                        });

                        return new
                        {
                            success = false,
                            msg = "Workflow execution failed",
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
                        msg = "Workflow execution failed",
                        failedAt = i,
                        failedStep = action,
                        totalSteps = safeSteps.Count,
                        completedSteps = i,
                        lastResult
                    };
                }

                try
                {
                    _logger.LogInformation("Starting workflow step execution. StepIndex={StepIndex}, Action={Action}", i, action);

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
                    _logger.LogError(ex, "Workflow step execution failed. StepIndex={StepIndex}, Action={Action}", i, action);

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
                            msg = "Workflow execution failed",
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
                        msg = "Workflow execution failed",
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
                    msg = "Workflow execution completed",
                    totalSteps = safeSteps.Count,
                    completedSteps = safeSteps.Count,
                    lastResult,
                    log
                };
            }

            return new
            {
                success = true,
                msg = "Workflow execution completed",
                totalSteps = safeSteps.Count,
                completedSteps = safeSteps.Count,
                lastResult
            };
        }




        /// <summary>
        /// Unified execution of builtin atomic skills
        ///
        /// Notes:
        /// 1. Handle builtin skills only
        /// 2. Workflow skills do not use this path; they use RunWorkflowAsync
        /// 3. Throw NotSupportedException if skillCode is unsupported
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
                _ => throw new NotSupportedException($"Unsupported skill: {skillCode}")
            };
        }




        #region Template Processing
        /// <summary>
        /// Template variable replacement
        ///
        /// Supports:
        /// - {{step0}}
        /// - {{step0.path}}
        /// - {{step0.data.path}}
        /// - {{myInputVar}}
        ///
        /// Notes:
        /// 1. Apply template replacement only to arguments that can be converted to strings
        /// 2. Preserve non-string values as-is
        /// 3. Replace missing template variables with an empty string
        /// 4. Support reading from:
        ///    - Dictionary<string, object>
        ///    - JsonElement
        ///    - Anonymous/regular object properties
        ///    to read fields
        /// </summary>
        private Dictionary<string, object> ResolveTemplateVariables(
            Dictionary<string, object>? args,
            IReadOnlyDictionary<string, object> context)
        {
            // 1. Null/empty guard
            if (args == null || args.Count == 0)
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            var resolved = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in args)
            {
                // 2. Try to convert the current argument to a string
                //    Only string-like arguments are subject to template replacement
                string? rawText = kv.Value switch
                {
                    null => null,
                    string s => s,
                    JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
                    JsonElement je => je.ToString(),
                    _ => kv.Value.ToString()
                };

                // 3. Preserve the original value if it cannot be converted to a string
                if (rawText == null)
                {
                    resolved[kv.Key] = kv.Value!;
                    continue;
                }

                // 4. Replace template variables
                // Replace {{ ... }} first
                var replaced = Regex.Replace(rawText, @"\{\{\s*([^}]+?)\s*\}\}", match =>
                {
                    var expr = match.Groups[1].Value.Trim();
                    return ResolveTemplateExpression(expr, context);
                });

                // Then support ${ ... } for compatibility
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
        /// Resolve a single template expression
        ///
        /// Examples:
        /// - step0
        /// - step0.path
        /// - step0.data.path
        /// - myInputVar
        ///
        /// Rules:
        /// 1. Retrieve the top-level variable first (such as step0/myInputVar)
        /// 2. Then read properties/fields one level at a time
        /// 3. Return an empty string if any level does not exist
        /// </summary>
        private string ResolveTemplateExpression(
            string expr,
            IReadOnlyDictionary<string, object> context)
        {
            if (string.IsNullOrWhiteSpace(expr))
                return "";

            // Split on dots, for example step0.data.path
            var parts = expr.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return "";

            // First retrieve the top-level variable from the context
            if (!context.TryGetValue(parts[0], out var current) || current == null)
                return "";

            // Traverse the value one level at a time
            for (int i = 1; i < parts.Length; i++)
            {
                current = GetObjectMemberValue(current, parts[i]);
                if (current == null)
                    return "";
            }

            return ConvertObjectToString(current);
        }

        /// <summary>
        /// Read a specified member value from an object (case-insensitive)
        ///
        /// Supports:
        /// 1. Dictionary<string, object>
        /// 2. JsonElement (object type)
        /// 3. Anonymous/regular object properties
        /// </summary>
        private object? GetObjectMemberValue(object obj, string memberName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            // 1. Dictionary<string, object> (case-insensitive lookup)
            if (obj is IDictionary<string, object> dict)
            {
                // Try a direct lookup first
                if (dict.TryGetValue(memberName, out var value))
                    return value;

                // Then perform a case-insensitive match
                var matchedKey = dict.Keys.FirstOrDefault(k =>
                    string.Equals(k, memberName, StringComparison.OrdinalIgnoreCase));

                if (matchedKey != null && dict.TryGetValue(matchedKey, out var matchedValue))
                    return matchedValue;

                return null;
            }

            // 2. JsonElement (object type, case-insensitive property names)
            if (obj is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Object)
                {
                    // Try a direct lookup first
                    if (je.TryGetProperty(memberName, out var child))
                        return child;

                    // Then iterate to perform a case-insensitive match
                    foreach (var prop in je.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, memberName, StringComparison.OrdinalIgnoreCase))
                            return prop.Value;
                    }
                }

                return null;
            }

            // 3. Regular/anonymous objects (case-insensitive property names)
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
        /// Safely convert an object to a string
        ///
        /// Supports:
        /// - string
        /// - JsonElement
        /// - Regular objects
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

        #region Atomic Skill: Email

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
                            Text = TryGetStringProperty(raw, "Text", "Email sent"),
                            ResultText = TryGetStringProperty(raw, "Text", "Email sent"),
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
                            $"{x.Index}. {x.Subject} | {x.From} | {x.DateText} | {(x.HasAttachments ? "Has attachments" : "No attachments")} | {(x.IsUnread ? "Unread" : "Read")}"
                        ).ToList();

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "search_email",
                            Text = items.Count == 0 ? "No matching emails found" : $"Found {items.Count} emails",
                            ResultText = items.Count == 0 ? "No matching emails found" : $"Found {items.Count} emails",
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
                            Text = $"Email read: {detail.Subject}",
                            ResultText = string.IsNullOrWhiteSpace(detail.TextPreview) ? "The email has no body content" : detail.TextPreview,
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
                            throw new ArgumentException("savePath cannot be empty");

                        var fullSavePath = ValidatePath(savePath, mustExist: false);
                        var files = await MailKitHelper.DownloadAttachmentsAsync(uid, fullSavePath);

                        return new SkillResult
                        {
                            Success = true,
                            SkillCode = "email_task",
                            Type = "download_attachments",
                            Text = files.Count == 0 ? "This email has no attachments" : $"Downloaded {files.Count} attachments",
                            ResultText = files.Count == 0 ? "This email has no attachments" : $"Downloaded {files.Count} attachments",
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
                            Text = "Marked as read",
                            ResultText = "Marked as read",
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
                            throw new ArgumentException("At least one of replyText or replyHtml must be provided");

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
                            Text = "Reply sent",
                            ResultText = "Reply sent",
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
                            throw new ArgumentException("filePath cannot be empty");

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
                            Text = "The email was saved as an EML file",
                            ResultText = "The email was saved as an EML file",
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
                    throw new NotSupportedException($"Unsupported email_task operation: {action}");
            }
        }

        private UniqueId ResolveUid(Dictionary<string, object> args, string scopedContextKey)
        {
            string mailRef = GetString(args, "mailRef");
            if (!string.IsNullOrWhiteSpace(mailRef))
            {
                if (MailKitHelper.TryParseMailRef(mailRef, out var uidByRef))
                    return uidByRef;

                throw new ArgumentException("mailRef has an invalid format");
            }

            int index = GetIntArg(args, "index", 0);
            if (index <= 0)
                throw new ArgumentException("mailRef or index must be provided");

            if (!_mailContext.TryGetValue(scopedContextKey, out var cache) || cache.Items.Count == 0)
                throw new ArgumentException("Email context not found. Run search first.");

            cache.LastAccessAt = DateTime.Now;

            var item = cache.Items.FirstOrDefault(x => x.Index == index);
            if (item == null)
                throw new ArgumentException($"Email #{index} does not exist in the context");

            if (!MailKitHelper.TryParseMailRef(item.MailRef, out var uid))
                throw new ArgumentException("The cached mailRef is invalid");

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




        #region Atomic Skill: Files


        /// <summary>
        /// File-related skills:
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

            // Add three search parameters
            string root = GetString(args, "root");
            bool recursive = GetBoolArg(args, "recursive", false);
            bool exactName = GetBoolArg(args, "exactName", false);

            return action.Trim().ToLowerInvariant() switch
            {
                "search" => await SearchFileAsync(
                    keyword,
                    ext,
                    root,
                    recursive,
                    exactName),

                "copy" => await CopyAsync(from, to),
                "move" => await MoveAsync(from, to),
                "copy_many" => await CopyManyAsync(args),
                "move_many" => await MoveManyAsync(args),
                "rename" => await RenameAsync(from, newName),
                "mkdir" => await CreateDirAsync(from),

                _ => throw new NotSupportedException(
                    $"Unsupported file_task operation: {action}")
            };
        }


        private async Task<object> DoOpenTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is required");

            var fullPath = ValidatePath(path, mustExist: true);

            if (!System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"Path not found: {fullPath}");

            await Task.Run(() =>
            {
                using var p = Process.Start(new ProcessStartInfo(fullPath)
                {
                    UseShellExecute = true
                });
            });

            return new
            {
                Success = true,
                SkillCode = "open_task",
                Type = "open_file",
                Text = $"File opened successfully: {fullPath}",
                ResultValue = fullPath
            };
        }




        private async Task<object> DoPrintTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("File path is required");

            var fullPath = ValidatePath(path, mustExist: true);
            if (!System.IO.File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {fullPath}");

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

            // Return standard JSON to indicate success to the agent
            return new
            {
                Success = true,
                SkillCode = "print_task",
                Type = "print_file",
                Text = $"Print sent successfully: {Path.GetFileName(fullPath)}",
                ResultValue = fullPath
            };
        }


        private async Task<object> DoFolderTaskAsync(Dictionary<string, object> args)
        {
            string source = GetString(args, "source");
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("Source directory is required");

            var fullSource = ValidatePath(source, mustExist: true);
            if (!Directory.Exists(fullSource))
                throw new DirectoryNotFoundException($"Directory not found: {fullSource}");

            int count = 0;

            await Task.Run(() =>
            {
                foreach (var file in Directory.GetFiles(fullSource))
                {
                    try
                    {
                        string ext = Path.GetExtension(file).TrimStart('.').ToUpperInvariant();
                        string targetDir = Path.Combine(fullSource, string.IsNullOrEmpty(ext) ? "No file extension" : ext);

                        Directory.CreateDirectory(targetDir);

                        string targetPath = Path.Combine(targetDir, Path.GetFileName(file));
                        System.IO.File.Move(file, targetPath);

                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to categorize file: {FilePath}", file);
                    }
                }
            });

            return $"Categorization completed. Moved {count} files.";
        }

        #endregion


        #region Atomic Skill: WeCom

        /// <summary>
        /// Send messages through a WeCom bot
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
                throw new ArgumentException("wechat_task action cannot be empty");

            switch (action)
            {
                case "text":
                    {
                        string content = GetString(args, "content");
                        if (string.IsNullOrWhiteSpace(content))
                            throw new ArgumentException("content cannot be empty in text mode");

                        bool isAtAll = bool.TryParse(GetString(args, "isAtAll", "false"), out var b) && b;

                        string atUsersRaw = GetString(args, "atUsers");
                        string[] atUsers = string.IsNullOrWhiteSpace(atUsersRaw)
                            ? Array.Empty<string>()
                            : atUsersRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        string result = await WechatBotHelper.SendText(content, isAtAll, atUsers);

                        return new SkillResult
                        {
                            Success = result.StartsWith("\u6210\u529f"),
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
                            Error = result.StartsWith("\u6210\u529f") ? "" : result
                        }.Normalize();
                    }

                case "markdown":
                    {
                        string content = GetString(args, "content");
                        if (string.IsNullOrWhiteSpace(content))
                            throw new ArgumentException("content cannot be empty in markdown mode");

                        string result = await WechatBotHelper.SendMarkdown(content);

                        return new SkillResult
                        {
                            Success = result.StartsWith("\u6210\u529f"),
                            SkillCode = "wechat_task",
                            Type = "markdown",
                            Text = result,
                            Data = new
                            {
                                action = "markdown",
                                content
                            },
                            Error = result.StartsWith("\u6210\u529f") ? "" : result
                        }.Normalize();
                    }

                case "card":
                    {
                        string title = GetString(args, "title");
                        string desc = GetString(args, "desc");
                        string url = GetString(args, "url");
                        string picUrl = GetString(args, "picUrl");

                        if (string.IsNullOrWhiteSpace(title))
                            throw new ArgumentException("title cannot be empty in card mode");
                        if (string.IsNullOrWhiteSpace(desc))
                            throw new ArgumentException("desc cannot be empty in card mode");
                        if (string.IsNullOrWhiteSpace(url))
                            throw new ArgumentException("url cannot be empty in card mode");

                        string result = await WechatBotHelper.SendCard(title, desc, url, picUrl);

                        return new SkillResult
                        {
                            Success = result.StartsWith("\u6210\u529f"),
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
                            Error = result.StartsWith("\u6210\u529f") ? "" : result
                        }.Normalize();
                    }

                default:
                    throw new NotSupportedException($"Unsupported wechat_task operation: {action}");
            }
        }

        #endregion

        #region Atomic Skill: Browser

        /// <summary>
        /// Browser skill
        ///
        /// Parameters:
        /// {
        ///   "actions": [ ...BrowserAction array... ]
        /// }
        ///
        /// Notes:
        /// 1. BrowserController is no longer called directly here
        /// 2. Call BrowserService.ExecuteActionAsync instead
        /// 3. Return the final step result for workflow references
        /// </summary>
        /// <summary>
        /// Browser skill
        ///
        /// Parameters:
        /// {
        ///   "actions": [ ...BrowserAction array... ]
        /// }
        ///
        /// Notes:
        /// 1. BrowserController is no longer called directly here
        /// 2. Call BrowserService.ExecuteActionAsync instead
        /// 3. Accept actions as either a JSON string or a JsonElement array
        /// 4. Support lowercase field names: type/url/selector/value
        /// </summary>
        /// <summary>
        /// Browser skill (compact response)
        ///
        /// Parameters:
        /// {
        ///   "actions": [ ...BrowserAction array... ],
        ///   "sessionId": "optional",
        ///   "closeSession": false,
        ///   "includeOutputs": false
        /// }
        ///
        /// Notes:
        /// 1. Accept actions as either a JSON string or a JsonElement array
        /// 2. Support lowercase field names: type/url/selector/value
        /// 3. Do not return full outputs by default to avoid oversized results
        /// 4. Return only sessionId, page, list, and result
        /// </summary>
        /// <summary>
        /// Browser skill (compact response by default)
        ///
        /// Parameters:
        /// {
        ///   "actions": [ ...BrowserAction array... ],
        ///   "sessionId": "optional",
        ///   "closeSession": false,
        ///   "includeOutputs": false
        /// }
        ///
        /// Notes:
        /// 1. Do not return full outputs by default to avoid oversized results
        /// 2. Return only sessionId, page, count, list, and result
        /// 3. Return per-action details only when includeOutputs=true
        /// </summary>
        private async Task<object> DoBrowserTaskAsync(Dictionary<string, object> args)
        {
            List<BrowserAction> actions;

            if (!args.TryGetValue("actions", out var actionsObj) || actionsObj == null)
                throw new ArgumentException("browser_task requires actions");

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
                    throw new ArgumentException("actions has an invalid format and must be a JSON array");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse browser_task actions JSON");
                throw new ArgumentException("Failed to parse actions JSON: " + ex.Message);
            }

            if (actions.Count == 0)
                throw new ArgumentException("browser_task actions cannot be empty");

            for (int i = 0; i < actions.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(actions[i].Type))
                    throw new ArgumentException($"The type of action #{i + 1} cannot be empty");
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

                // Add a consistent count field for direct use by workflows and AI
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

        #region Atomic Skill: External Tools

        /// <summary>
        /// Invoke an external executable from the allowlist
        /// </summary>
        private async Task<object> RunExternalToolAsync(Dictionary<string, object> args)
        {
            string exePath = GetString(args, "exePath");
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("Path is required");

            string exeName = Path.GetFileName(exePath);
            if (!AllowedExeNames.Contains(exeName))
                throw new UnauthorizedAccessException($"Tool {exeName} is not on the allowlist");

            var fullExePath = ValidatePath(exePath, mustExist: true);
            if (!System.IO.File.Exists(fullExePath))
                throw new FileNotFoundException($"Tool does not exist: {fullExePath}");

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

                throw new TimeoutException($"External tool execution timed out after {timeoutSec} seconds");
            }

            string output = await outputTask;
            string error = await errorTask;

            return new SkillResult
            {
                Success = process.ExitCode == 0,
                SkillCode = "tool_task",
                Type = "run_exe",
                Text = process.ExitCode == 0 ? "Tool execution completed" : "Tool execution failed",
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

        #region File Search

        /// <summary>
        /// Search for files:
        /// Prefer Everything, fall back to Windows Search, then use recursive search
        /// </summary>
        private async Task<SkillResult> SearchFileAsync(
    string keyword,
    string ext = "*",
    string root = "",
    bool recursive = false,
    bool exactName = false)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                throw new ArgumentException("The search keyword cannot be empty");

            List<string> resultList;

            // When root is specified, search only that directory; do not call Everything,
            // Windows Search, or a full-disk recursive search.
            if (!string.IsNullOrWhiteSpace(root))
            {
                resultList = await SearchInDirectoryAsync(
                    root,
                    keyword,
                    ext,
                    recursive,
                    exactName);
            }
            else
            {
                resultList = new List<string>();

                try
                {
                    resultList = await SearchWithEverythingAsync(keyword, ext);

                    if (!resultList.Any())
                    {
                        resultList = await SearchWithWindowsSearchAsync(
                            keyword,
                            ext);
                    }
                }
                catch
                {
                    resultList = await SearchFallbackAsync(keyword, ext);
                }
            }

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
                    Text = "No files found",
                    ResultText = "No files found",
                    ResultList = new List<string>(),
                    ResultValue = "",
                    Data = new
                    {
                        keyword,
                        ext,
                        root,
                        firstPath = "",
                        paths = Array.Empty<string>(),
                        count = 0
                    },
                    Error = "No files found"
                }.Normalize();
            }

            var firstPath = resultList[0];

            return new SkillResult
            {
                Success = true,
                SkillCode = "file_task",
                Type = "search",
                Text = $"Found {resultList.Count} files",
                ResultText = $"Found {resultList.Count} files",
                ResultList = resultList,
                ResultValue = firstPath,
                Data = new
                {
                    keyword,
                    ext,
                    root,
                    firstPath,
                    paths = resultList,
                    count = resultList.Count
                }
            }.WithList(resultList);
        }

        private async Task<List<string>> SearchInDirectoryAsync(
    string root,
    string keyword,
    string ext,
    bool recursive,
    bool exactName)
        {
            var fullRoot = ValidatePath(root, mustExist: true);

            if (!Directory.Exists(fullRoot))
                throw new DirectoryNotFoundException(
                    $"The search directory does not exist.：{fullRoot}");

            return await Task.Run(() =>
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    MatchCasing = MatchCasing.CaseInsensitive,
                    AttributesToSkip =
                        FileAttributes.Hidden |
                        FileAttributes.System
                };

                var pattern = string.IsNullOrWhiteSpace(ext) || ext == "*"
                    ? "*"
                    : $"*.{ext.TrimStart('.')}";

                return Directory
                    .EnumerateFiles(fullRoot, pattern, options)
                    .Where(file =>
                    {
                        var fileName = Path.GetFileName(file);

                        return exactName
                            ? string.Equals(
                                fileName,
                                keyword,
                                StringComparison.OrdinalIgnoreCase)
                            : fileName.Contains(
                                keyword,
                                StringComparison.OrdinalIgnoreCase);
                    })
                    .Select(Path.GetFullPath)
                    .ToList();
            });
        }

        /// <summary>
        /// Everything search
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
                    // Note: in many Everything SDKs:
                    // item.Path = directory
                    // item.Name = file name
                    string dir = item.Path ?? "";
                    string name = item.Name ?? "";

                    string fullPath = string.IsNullOrWhiteSpace(name)
                        ? dir
                        : Path.Combine(dir, name);

                    if (string.IsNullOrWhiteSpace(fullPath))
                        continue;

                    // Keep actual files only, not directories
                    if (!System.IO.File.Exists(fullPath))
                        continue;

                    // Filter out shortcuts to avoid returning .lnk files from Recent
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
                    // Ignore invalid paths or inaccessible items
                }
            }

            return list;
        }


        /// <summary>
        /// Windows Search
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
                    // Ignore invalid paths or inaccessible items
                }
            }

            return list;
        }

        /// <summary>
        /// Fallback recursive search
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
                            // Filter out shortcuts
                            if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                                continue;

                            ValidatePath(file, mustExist: true);
                            list.Add(file);
                        }
                        catch
                        {
                            // Ignore invalid paths or inaccessible items
                        }
                    }
                }
                catch
                {
                    // Ignore failures while searching an individual directory
                }
            }

            return await Task.FromResult(list);
        }


        /// <summary>
        /// Format a file list as multiline text for easier AI consumption
        /// </summary>
        private string FormatFileList(List<string> files)
        {
            if (files == null || files.Count == 0)
                return "The file was not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"find {files.Count} files：");

            foreach (var file in files)
                //sb.AppendLine("✅ " + file);
                sb.AppendLine(file);

            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Secure File Operations

        /// <summary>
        /// Path security validation
        ///
        /// Notes:
        /// 1. Only paths under AllowedRoots may be accessed
        /// 2. Optionally require the path to exist
        /// </summary>
        private string ValidatePath(string inputPath, bool mustExist = false)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("The path must not be empty.");

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
                throw new UnauthorizedAccessException("The path is outside the allowed scope.");

            if (mustExist && !System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"The item does not exist.: {fullPath}");

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
                Text = "The file has been copied successfully.",
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
                Text = "The file has been moved successfully.",
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
                throw new ArgumentException("The new filename must not be empty.");

            // Ensure newName contains no path; keep only the file name
            var safeNewName = Path.GetFileName(newName);

            var sourceDir = Path.GetDirectoryName(source);
            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentException("Failed to retrieve the source file directory.");

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
                Text = "The file has been renamed successfully.",
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
                Text = "Directory created",
                Data = new
                {
                    path = fullPath
                }
            }.WithValue(fullPath);
        }

        #region Batch File Operations
        private async Task<SkillResult> CopyManyAsync(Dictionary<string, object> args)
        {
            var paths = GetStringList(args, "paths");
            string toDir = GetString(args, "toDir");
            bool overwrite = bool.TryParse(GetString(args, "overwrite", "true"), out var ov) && ov;

            if (paths.Count == 0)
                throw new ArgumentException("paths cannot be empty");

            if (string.IsNullOrWhiteSpace(toDir))
                throw new ArgumentException("toDir cannot be empty");

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
                            throw new FileNotFoundException($"File does not exist: {source}");

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
                    ? $"Batch copy completed: {successCount} succeeded, {failed.Count} failed"
                    : "Batch copy failed",
                Data = new
                {
                    toDir = fullTargetDir,
                    successCount,
                    failedCount = failed.Count,
                    copied,
                    failed
                },
                Error = successCount > 0 ? "" : "No files were copied successfully"
            }.Normalize();
        }

        private async Task<SkillResult> MoveManyAsync(Dictionary<string, object> args)
        {
            var paths = GetStringList(args, "paths");
            string toDir = GetString(args, "toDir");
            bool overwrite = bool.TryParse(GetString(args, "overwrite", "true"), out var ov) && ov;

            if (paths.Count == 0)
                throw new ArgumentException("paths cannot be empty");

            if (string.IsNullOrWhiteSpace(toDir))
                throw new ArgumentException("toDir cannot be empty");

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
                            throw new FileNotFoundException($"The file was not found.: {source}");

                        var target = Path.Combine(fullTargetDir, Path.GetFileName(source));
                        var validatedTarget = ValidatePath(target, mustExist: false);

                        if (System.IO.File.Exists(validatedTarget))
                        {
                            if (overwrite)
                                System.IO.File.Delete(validatedTarget);
                            else
                                throw new IOException($"Target file already exists: {validatedTarget}");
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
                    ? $"Batch move completed: {successCount} succeeded, {failed.Count} failed"
                    : "Batch move failed",
                Data = new
                {
                    toDir = fullTargetDir,
                    successCount,
                    failedCount = failed.Count,
                    moved,
                    failed
                },
                Error = successCount > 0 ? "" : "No files were moved successfully"
            }.Normalize();
        }

        #endregion


        #endregion

        #region Utility Methods

        /// <summary>
        /// Safely retrieve a string from Dictionary<string, object>
        /// Supports string, JsonElement, and regular object values
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

        #region Management Endpoints

        [HttpPost("GetAllSkillCodes")]
        public async Task<IActionResult> GetAllSkillCodes()
        {
            var codes = await QueryAsync<string>("SELECT SkillCode FROM Skills");
            return Ok(ResponseHelper.Success(codes));
        }


        /// <summary>
        /// Save a skill definition
        /// Update if it exists; insert otherwise
        /// </summary>
        [HttpPost("SaveSkillAction")]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel model)
        {
            if (string.IsNullOrWhiteSpace(model?.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode is required"));

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

            return Ok(ResponseHelper.Success("Saved successfully."));
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
            return Ok(ResponseHelper.Success("Deleted successfully"));
        }

        [HttpPost("ExecSql")]
        public IActionResult ExecSql()
        {
            return StatusCode(403, ResponseHelper.Fail<object>("Disabled"));
        }

        #endregion
    }

    #region Models


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
        /// Skill type; defaults to OtherType when omitted by the frontend
        /// </summary>
        public string SkillType { get; set; } = "OtherType";

        /// <summary>
        /// Update time; retained as a string for compatibility with the existing database table
        /// </summary>
        public string UpdateTime { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public class ExecSkillModel
    {
        public string SkillCode { get; set; } = "";

        public Dictionary<string, object> Arguments { get; set; } = new();

        /// <summary>
        /// Temporary workflow steps.
        /// If Steps is provided, execute it directly without querying the database.
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
