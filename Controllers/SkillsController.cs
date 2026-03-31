using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TangYuan.Models;
using WebApi.Tools;

namespace TangYuan.Controllers
{
    [Authorize(AuthenticationSchemes = "ApiKey")]
    [Route("api/[controller]")]
    [ApiController]
    public class SkillsController : BaseCommandController
    {
        private readonly ILogger<SkillsController> _logger;
        private readonly FileSystemOptions _fsOptions;
        private static readonly HashSet<string> AllowedExeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "sendmail.exe",
            "pdf2txt.exe",
            "magick.exe"
        };

        public SkillsController(
            IConfiguration configuration,
            ILogger<SkillsController> logger,            
            IOptions<FileSystemOptions> fsOptions)
            : base(configuration, logger)
        {
            _logger = logger;
            _fsOptions = fsOptions.Value;
        }

        [HttpPost("GetSkillListForAI")]
        public async Task<IActionResult> GetSkillListForAI()
        {
            try
            {
                var sql = "SELECT SkillCode, Remark AS AIDesc FROM Skills ORDER BY ID ASC";
                var list = (await QueryAsync<dynamic>(sql)).ToList();

                list.Add(new { SkillCode = "file_task", AIDesc = "文件：搜索、复制、移动、删除、创建目录" });
                list.Add(new { SkillCode = "open_task", AIDesc = "打开文件、程序、文件夹" });
                list.Add(new { SkillCode = "print_task", AIDesc = "打印 Word/Excel/PDF/图片" });
                list.Add(new { SkillCode = "folder_task", AIDesc = "按后缀自动归类文件" });
                list.Add(new { SkillCode = "tool_task", AIDesc = "调用外部命令行工具 exe，支持传参" });

                return Ok(ResponseHelper.Success(list));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取技能列表失败");
                return StatusCode(500, ResponseHelper.Fail<object>("获取技能列表失败"));
            }
        }

        [HttpPost("ExecuteSkill")]
        public async Task<IActionResult> ExecuteSkill([FromBody] ExecSkillModel model)
        {
            if (model == null)
                return BadRequest(ResponseHelper.Fail<object>("请求体不能为空"));

            string code = model.SkillCode?.Trim();
            if (string.IsNullOrEmpty(code))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            try
            {
                var skillJson = await QueryFirstOrDefaultAsync<string>(
                    "SELECT SkillActions FROM Skills WHERE SkillCode = @SkillCode LIMIT 1",
                    new { SkillCode = code });

                if (!string.IsNullOrEmpty(skillJson))
                {
                    var steps = JsonSerializer.Deserialize<List<SkillStep>>(skillJson);
                    var result = await RunWorkflowAsync(steps, model.Arguments ?? new());
                    return Ok(ResponseHelper.Success(result));
                }

                // 原子技能调用
                object res = code.ToLower() switch
                {
                    "file_task" => await DoFileTaskAsync(model.Arguments),
                    "open_task" => await DoOpenTaskAsync(model.Arguments),
                    "print_task" => await DoPrintTaskAsync(model.Arguments),
                    "folder_task" => await DoFolderTaskAsync(model.Arguments),
                    "tool_task" => await RunExternalToolAsync(model.Arguments),
                    _ => "无效技能"
                };

                return Ok(ResponseHelper.Success(res));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "安全限制：{Message}", ex.Message);
                return StatusCode(403, ResponseHelper.Fail<object>(ex.Message));
            }
            catch (FileNotFoundException ex)
            {
                _logger.LogWarning(ex, "文件不存在：{Message}", ex.Message);
                return NotFound(ResponseHelper.Fail<object>(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行技能异常，SkillCode={SkillCode}", model.SkillCode);
                return StatusCode(500, ResponseHelper.Fail<object>("服务器内部错误"));
            }
        }

        #region 原子技能实现（异步安全版本）

        private async Task<object> DoFileTaskAsync(Dictionary<string, object> args)
        {
            string act = GetString(args, "action", "");
            string from = GetString(args, "from", "");
            string to = GetString(args, "to", "");
            string keyword = GetString(args, "keyword", "");
            string ext = GetString(args, "ext", "*");

            return act.ToLower() switch
            {
                "search" => await SearchFileAsync(keyword, ext),
                "copy" => await CopyAsync(from, to),
                "move" => await MoveAsync(from, to),
               // "delete" => await DeleteAsync(from), 禁止删除操作
                "mkdir" => await CreateDirAsync(from),
                _ => "不支持的操作"
            };
        }

        private async Task<object> DoOpenTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path", "");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("路径不能为空");

            var fullPath = ValidatePath(path, mustExist: true);
            if (!System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"路径不存在: {fullPath}");

            await Task.Run(() =>
            {
                using var p = Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            });
            return "已打开：" + fullPath;
        }

        private async Task<object> DoPrintTaskAsync(Dictionary<string, object> args)
        {
            string path = GetString(args, "path", "");
            if (string.IsNullOrEmpty(path))
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
            string source = GetString(args, "source", "");
            if (string.IsNullOrEmpty(source))
                throw new ArgumentException("源目录不能为空");

            var fullSource = ValidatePath(source, mustExist: true);
            if (!Directory.Exists(fullSource))
                throw new DirectoryNotFoundException($"目录不存在: {fullSource}");

            int count = 0;
            await Task.Run(() =>
            {
                foreach (var f in Directory.GetFiles(fullSource))
                {
                    try
                    {
                        string ext = Path.GetExtension(f).TrimStart('.').ToUpper();
                        string dir = Path.Combine(fullSource, string.IsNullOrEmpty(ext) ? "无后缀" : ext);
                        Directory.CreateDirectory(dir);
                        System.IO.File.Move(f, Path.Combine(dir, Path.GetFileName(f)));
                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "归类文件失败: {FilePath}", f);
                    }
                }
            });
            return $"归类完成，共移动 {count} 个文件";
        }

        private async Task<object> RunExternalToolAsync(Dictionary<string, object> args)
        {
            string exePath = GetString(args, "exePath", "");
            if (string.IsNullOrEmpty(exePath))
                throw new ArgumentException("工具路径不能为空");

            string exeName = Path.GetFileName(exePath);
            if (!AllowedExeNames.Contains(exeName))
                throw new UnauthorizedAccessException($"工具 {exeName} 未在白名单中");

            var fullExePath = ValidatePath(exePath, mustExist: true);
            if (!System.IO.File.Exists(fullExePath))
                throw new FileNotFoundException($"工具不存在: {fullExePath}");

            string arguments = GetString(args, "arguments", "");
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

            await process.WaitForExitAsync(cts.Token);
            string output = await outputTask;
            string error = await errorTask;

            return new
            {
                status = process.HasExited ? "完成" : "超时",
                exitCode = process.ExitCode,
                output = string.IsNullOrEmpty(output) ? "无输出" : output,
                error = string.IsNullOrEmpty(error) ? "无错误" : error
            };
        }

        #endregion

        #region 混合搜索（Everything → Windows Search → 递归降级）

        /// <summary>
        /// 搜索文件，优先使用 Everything，失败则尝试 Windows Search，最后降级为递归搜索
        /// </summary>
        private async Task<string> SearchFileAsync(string keyword, string ext = "*")
        {
            if (string.IsNullOrEmpty(keyword))
                throw new ArgumentException("搜索关键词不能为空");

            string result = "未找到文件";

            // 1. 尝试 Everything
            try
            {
                result = await SearchWithEverythingAsync(keyword, ext);
                if (!string.IsNullOrEmpty(result) && result != "未找到文件" && result != "搜索发生错误")
                    return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Everything 搜索失败，自动降级到 Windows Search");
            }

            // 2. 尝试 Windows Search
            try
            {
                result = await SearchWithWindowsSearchAsync(keyword, ext);
                if (!string.IsNullOrEmpty(result) && result != "未找到文件")
                    return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Windows Search 搜索失败，自动降级到递归搜索");
            }

            // 3. 最终降级：递归遍历
            return await SearchFallbackAsync(keyword, ext);
        }

        private async Task<string> SearchWithEverythingAsync(string keyword, string ext = "*")
        {
            try
            {
                var searchClient = new EverythingSearchClient.SearchClient();
                var results = await Task.Run(() => searchClient.Search(keyword));

                if (results.Items.Length == 0)
                    return "未找到文件";

                return results.Items[0].Path;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Everything 搜索异常，将降级");
                // 抛出异常，让上层捕获并降级
                throw new Exception("Everything 不可用", ex);
            }
        }

        private async Task<string> SearchWithWindowsSearchAsync(string keyword, string ext = "*")
        {
            try
            {
                // 转义关键词中的双引号，防止SQL注入/语法错误
                string safeKeyword = keyword.Replace("\"", "\"\"");

                string query = $@"
            SELECT System.ItemPathDisplay 
            FROM SYSTEMINDEX 
            WHERE CONTAINS(System.FileName, ""{safeKeyword}"")";

                if (!string.IsNullOrEmpty(ext) && ext != "*")
                    query += $" AND System.FileExtension = '.{ext.TrimStart('.')}'";
                query += " ORDER BY System.DateModified DESC";

                using var conn = new OleDbConnection("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
                using var cmd = new OleDbCommand(query, conn);

                await conn.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string path = reader.GetString(0);
                    ValidatePath(path, mustExist: true);
                    return path;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Windows Search 异常，直接降级到递归搜索");
                // 抛出异常，让外层捕获并走最终降级
                throw;
            }

            return "未找到文件";
        }

        private async Task<string> SearchFallbackAsync(string keyword, string ext = "*")
        {
            var dirs = new[] {
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        @"D:\", @"E:\"
    };

            var opt = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var file = Directory.EnumerateFiles(dir, $"*.{ext}", opt)
                        .FirstOrDefault(f =>
                            Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));

                    if (file != null)
                    {
                        ValidatePath(file, mustExist: true);
                        return await Task.FromResult(file);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "递归搜索目录失败: {Directory}", dir);
                }
            }

            return "未找到文件";
        }

        #endregion


        #region 工作流（支持变量传递）

        private async Task<object> RunWorkflowAsync(List<SkillStep> steps, Dictionary<string, object> input)
        {
            if (steps == null || steps.Count == 0)
                throw new ArgumentException("工作流步骤不能为空");

            var context = new Dictionary<string, object>(input);
            var log = new List<object>();

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var resolvedArgs = ResolveTemplateVariables(step.Args, context, i);

                try
                {
                    object result = step.Action?.ToLower() switch
                    {
                        "search" => await SearchFileAsync(
                            GetString(resolvedArgs, "keyword", ""),
                            GetString(resolvedArgs, "ext", "*")),
                        "print" => await DoPrintTaskAsync(resolvedArgs),
                        "copy" => await CopyAsync(
                            GetString(resolvedArgs, "from", ""),
                            GetString(resolvedArgs, "to", "")),
                        "move" => await MoveAsync(
                            GetString(resolvedArgs, "from", ""),
                            GetString(resolvedArgs, "to", "")),
                        "delete" => await DeleteAsync(GetString(resolvedArgs, "path", "")),
                        "mkdir" => await CreateDirAsync(GetString(resolvedArgs, "path", "")),
                        "tool" => await RunExternalToolAsync(resolvedArgs),
                        "open" => await DoOpenTaskAsync(resolvedArgs),
                        _ => throw new NotSupportedException($"未知动作: {step.Action}")
                    };

                    context[$"step{i}"] = result;
                    log.Add(new { step = step.Action, result });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "工作流步骤 {Action} 执行失败", step.Action);
                    log.Add(new { step = step.Action, error = ex.Message });
                    return new { msg = "技能流程中断", log = log, failedAt = step.Action };
                }
            }
            return new { msg = "技能流程执行完毕", log = log };
        }

        private Dictionary<string, object> ResolveTemplateVariables(
            Dictionary<string, object> args,
            IReadOnlyDictionary<string, object> context,
            int currentStepIndex)
        {
            if (args == null) return new Dictionary<string, object>();

            var resolved = new Dictionary<string, object>();
            foreach (var kv in args)
            {
                if (kv.Value is string strValue)
                {
                    string newValue = Regex.Replace(strValue, @"\{\{([^}]+)\}\}", match =>
                    {
                        string placeholder = match.Groups[1].Value.Trim();
                        if (placeholder.StartsWith("step", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(placeholder.Substring(4), out int stepIdx) &&
                                context.TryGetValue($"step{stepIdx}", out var val))
                                return val?.ToString() ?? "";
                        }
                        else if (placeholder.StartsWith("input.", StringComparison.OrdinalIgnoreCase))
                        {
                            string key = placeholder.Substring(6);
                            if (context.TryGetValue(key, out var val))
                                return val?.ToString() ?? "";
                        }
                        return match.Value;
                    });
                    resolved[kv.Key] = newValue;
                }
                else
                {
                    resolved[kv.Key] = kv.Value;
                }
            }
            return resolved;
        }

        #endregion

        #region 安全文件操作

        private string ValidatePath(string inputPath, bool mustExist = false)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
                throw new ArgumentException("路径不能为空");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(inputPath);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"路径格式无效: {inputPath}", ex);
            }

            // 防止路径遍历攻击
            if (fullPath.Contains(".."))
            {
                throw new UnauthorizedAccessException("路径中包含非法字符：..");
            }

            if (_fsOptions.AllowedRoots != null && _fsOptions.AllowedRoots.Count > 0)
            {
                bool isAllowed = _fsOptions.AllowedRoots.Any(root =>
                    fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
                if (!isAllowed)
                    throw new UnauthorizedAccessException($"路径 {fullPath} 不在允许的根目录中");
            }

            if (mustExist && !System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"文件或目录不存在: {fullPath}");

            return fullPath;
        }


        private async Task<string> CopyAsync(string from, string to)
        {
            var fullFrom = ValidatePath(from, mustExist: true);
            var fullTo = ValidatePath(to, mustExist: false);
            await Task.Run(() => System.IO.File.Copy(fullFrom, fullTo, true));
            return "已复制";
        }

        private async Task<string> MoveAsync(string from, string to)
        {
            var fullFrom = ValidatePath(from, mustExist: true);
            var fullTo = ValidatePath(to, mustExist: false);
            await Task.Run(() => System.IO.File.Move(fullFrom, fullTo));
            return "已移动";
        }

        private async Task<string> DeleteAsync(string path)
        {
            var fullPath = ValidatePath(path, mustExist: true);
            await Task.Run(() => System.IO.File.Delete(fullPath));
            return "已删除";
        }

        private async Task<string> CreateDirAsync(string path)
        {
            var fullPath = ValidatePath(path, mustExist: false);
            await Task.Run(() => Directory.CreateDirectory(fullPath));
            return "已创建";
        }

        private string GetString(Dictionary<string, object> args, string key, string defaultValue = "")
        {
            if (args == null) return defaultValue;
            return args.TryGetValue(key, out var value) ? value?.ToString() ?? defaultValue : defaultValue;
        }

        #endregion

        #region 管理接口

        [HttpPost("GetAllSkillCodes")]
        public async Task<IActionResult> GetAllSkillCodes()
        {
            var codes = await QueryAsync<string>("SELECT SkillCode FROM Skills");
            return Ok(ResponseHelper.Success(codes));
        }

        [HttpPost("GetSkillAction")]
        public async Task<IActionResult> GetSkillAction([FromBody] SkillModel m)
        {
            if (m == null || string.IsNullOrEmpty(m.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            var action = await QueryFirstOrDefaultAsync<string>(
                "SELECT SkillActions FROM Skills WHERE SkillCode = @SkillCode", m);
            return Ok(ResponseHelper.Success(action));
        }

        [HttpPost("SaveSkillAction")]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel m)
        {
            if (m == null || string.IsNullOrEmpty(m.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("SkillCode 不能为空"));

            var sql = @"INSERT INTO Skills (SkillCode, SkillActions, Remark)
                        VALUES (@SkillCode, @SkillActions, @Remark)
                        ON CONFLICT(SkillCode) DO UPDATE SET
                        SkillActions = excluded.SkillActions,
                        Remark = excluded.Remark";
            await ExecuteAsync(sql, m);
            return Ok(ResponseHelper.Success("保存成功"));
        }

        [HttpPost("GetSkillList")]
        public async Task<IActionResult> GetSkillList()
        {
            var skills = await QueryAsync<dynamic>("SELECT * FROM Skills");
            return Ok(ResponseHelper.Success(skills));
        }

        [HttpPost("DeleteSkill")]
        public async Task<IActionResult> DeleteSkill([FromBody] SkillModel m)
        {
            if (m == null || m.ID <= 0 && string.IsNullOrEmpty(m.SkillCode))
                return BadRequest(ResponseHelper.Fail<object>("缺少有效标识"));

            string sql = m.ID > 0
                ? "DELETE FROM Skills WHERE ID = @ID"
                : "DELETE FROM Skills WHERE SkillCode = @SkillCode";
            await ExecuteAsync(sql, m);
            return Ok(ResponseHelper.Success("删除成功"));
        }

        [HttpPost("ExecSql")]
        public IActionResult ExecSql()
        {
            return StatusCode(403, ResponseHelper.Fail<object>("此接口已禁用"));
        }

        #endregion
    }

    #region 模型定义

    public class SkillModel
    {
        public int ID { get; set; }
        public string SkillCode { get; set; }
        public string SkillActions { get; set; }
        public string Remark { get; set; }
    }

    public class ExecSkillModel
    {
        public string SkillCode { get; set; }
        public Dictionary<string, object> Arguments { get; set; } = new();
    }

    public class SkillStep
    {
        public string Action { get; set; }
        public Dictionary<string, object> Args { get; set; }
    }

    public class FileSystemOptions
    {
        public List<string> AllowedRoots { get; set; } = new()
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            @"C:\Temp\AIWorkspace"
        };
    }

    #endregion
}