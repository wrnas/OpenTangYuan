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
using TangYuan.Tools;
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
                list.Add(new { SkillCode = "lock_task", AIDesc = "一键锁屏" });
                list.Add(new { SkillCode = "screenshot_task", AIDesc = "全屏截图" });
                list.Add(new { SkillCode = "email_task", AIDesc = "发送邮件（可带附件）" });

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

                object res = code.ToLower() switch
                {
                    "file_task" => await DoFileTaskAsync(model.Arguments),
                    "open_task" => await DoOpenTaskAsync(model.Arguments),
                    "print_task" => await DoPrintTaskAsync(model.Arguments),
                    "folder_task" => await DoFolderTaskAsync(model.Arguments),
                    "tool_task" => await RunExternalToolAsync(model.Arguments),
                    "lock_task" => await CommandTools.LockScreenAsync(),
                    "screenshot_task" => await CommandTools.CaptureScreenAsync(),
                    "email_task" => await CommandTools.SendEmailAsync(model.Arguments),
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

        #region 原子技能实现
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

        #region 文件搜索（返回全部匹配结果 · 修复版）
        /// <summary>
        /// 搜索文件：优先 Everything → 降级 Windows Search → 最后递归搜索
        /// 返回：所有匹配的文件路径列表（多行字符串）
        /// </summary>
        private async Task<string> SearchFileAsync(string keyword, string ext = "*")
        {
            if (string.IsNullOrEmpty(keyword))
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
        /// Everything 搜索：返回所有匹配文件
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
                    if (!string.IsNullOrEmpty(ext) && ext != "*" && !path.EndsWith($".{ext.Trim('.')}", StringComparison.OrdinalIgnoreCase))
                        continue;

                    ValidatePath(path, true);
                    list.Add(path);
                }
                catch { continue; }
            }

            return list;
        }

        /// <summary>
        /// Windows Search 搜索：返回所有匹配
        /// </summary>
        private async Task<List<string>> SearchWithWindowsSearchAsync(string keyword, string ext = "*")
        {
            var list = new List<string>();
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
                try
                {
                    string path = reader.GetString(0);
                    ValidatePath(path, true);
                    list.Add(path);
                }
                catch { continue; }
            }

            return list;
        }

        /// <summary>
        /// 兜底递归搜索：返回所有匹配
        /// </summary>
        private async Task<List<string>> SearchFallbackAsync(string keyword, string ext = "*")
        {
            var list = new List<string>();
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
                    var files = Directory.EnumerateFiles(dir, $"*.{ext}", opt)
                        .Where(f => Path.GetFileName(f).Contains(keyword, StringComparison.OrdinalIgnoreCase));

                    foreach (var file in files)
                    {
                        try
                        {
                            ValidatePath(file, true);
                            list.Add(file);
                        }
                        catch { continue; }
                    }
                }
                catch { continue; }
            }

            return await Task.FromResult(list);
        }

        /// <summary>
        /// 把文件列表格式化成多行文本，方便 AI 阅读
        /// </summary>
        private string FormatFileList(List<string> files)
        {
            if (files == null || files.Count == 0)
                return "未找到文件";

            var sb = new StringBuilder();
            sb.AppendLine($"找到 {files.Count} 个文件：");

            foreach (var f in files)
                sb.AppendLine("✅ " + f);

            return sb.ToString().TrimEnd();
        }
        #endregion

        #region 工作流
        private async Task<object> RunWorkflowAsync(List<SkillStep> steps, Dictionary<string, object> input)
        {
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
                        "search" => await SearchFileAsync(GetString(resolvedArgs, "keyword", ""), GetString(resolvedArgs, "ext", "*")),
                        "print" => await DoPrintTaskAsync(resolvedArgs),
                        "copy" => await CopyAsync(GetString(resolvedArgs, "from", ""), GetString(resolvedArgs, "to", "")),
                        "move" => await MoveAsync(GetString(resolvedArgs, "from", ""), GetString(resolvedArgs, "to", "")),
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

        private Dictionary<string, object> ResolveTemplateVariables(Dictionary<string, object> args, IReadOnlyDictionary<string, object> context, int currentStepIndex)
        {
            var resolved = new Dictionary<string, object>();
            foreach (var kv in args)
            {
                if (kv.Value is string strValue)
                {
                    resolved[kv.Key] = Regex.Replace(strValue, @"\{\{([^}]+)\}\}", match =>
                    {
                        string key = match.Groups[1].Value.Trim();
                        if (key.StartsWith("step") && int.TryParse(key.AsSpan(4), out int idx) && context.TryGetValue($"step{idx}", out var v))
                            return v?.ToString() ?? "";
                        if (key.StartsWith("input.") && context.TryGetValue(key.Substring(6), out var iv))
                            return iv?.ToString() ?? "";
                        return match.Value;
                    });
                }
                else resolved[kv.Key] = kv.Value;
            }
            return resolved;
        }
        #endregion

        #region 安全文件操作
        private string ValidatePath(string inputPath, bool mustExist = false)
        {
            if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("路径不能为空");
            string fullPath = Path.GetFullPath(inputPath);
            if (fullPath.Contains("..")) throw new UnauthorizedAccessException("路径非法");
            if (_fsOptions.AllowedRoots?.Any(r => fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)) == false)
                throw new UnauthorizedAccessException("路径不在允许范围");
            if (mustExist && !System.IO.File.Exists(fullPath) && !Directory.Exists(fullPath))
                throw new FileNotFoundException($"不存在: {fullPath}");
            return fullPath;
        }

        private async Task<string> CopyAsync(string from, string to)
        {
            var f = ValidatePath(from, true);
            var t = ValidatePath(to, false);
            await Task.Run(() => System.IO.File.Copy(f, t, true));
            return "已复制";
        }

        private async Task<string> MoveAsync(string from, string to)
        {
            var f = ValidatePath(from, true);
            var t = ValidatePath(to, false);
            await Task.Run(() => System.IO.File.Move(f, t));
            return "已移动";
        }

        private async Task<string> CreateDirAsync(string path)
        {
            var p = ValidatePath(path, false);
            await Task.Run(() => Directory.CreateDirectory(p));
            return "已创建";
        }

        private new string GetString(Dictionary<string, object> args, string key, string defaultValue = "")
        {
            return args != null && args.TryGetValue(key, out var v) ? v?.ToString() ?? defaultValue : defaultValue;
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
            if (string.IsNullOrEmpty(m?.SkillCode)) return BadRequest();
            var a = await QueryFirstOrDefaultAsync<string>("SELECT SkillActions FROM Skills WHERE SkillCode = @SkillCode", m);
            return Ok(ResponseHelper.Success(a));
        }

        [HttpPost("SaveSkillAction")]
        public async Task<IActionResult> SaveSkillAction([FromBody] SkillModel m)
        {
            if (string.IsNullOrEmpty(m?.SkillCode)) return BadRequest();
            var sql = @"INSERT INTO Skills (SkillCode,SkillActions,Remark) VALUES (@SkillCode,@SkillActions,@Remark)
                        ON CONFLICT(SkillCode) DO UPDATE SET SkillActions=excluded.SkillActions,Remark=excluded.Remark";
            await ExecuteAsync(sql, m);
            return Ok(ResponseHelper.Success("保存成功"));
        }

        [HttpPost("GetSkillList")]
        public async Task<IActionResult> GetSkillList()
        {
            var list = await QueryAsync<dynamic>("SELECT * FROM Skills");
            return Ok(ResponseHelper.Success(list));
        }

        [HttpPost("DeleteSkill")]
        public async Task<IActionResult> DeleteSkill([FromBody] SkillModel m)
        {
            if (m == null || (m.ID <= 0 && string.IsNullOrEmpty(m.SkillCode))) return BadRequest();
            string sql = m.ID > 0 ? "DELETE FROM Skills WHERE ID=@ID" : "DELETE FROM Skills WHERE SkillCode=@SkillCode";
            await ExecuteAsync(sql, m);
            return Ok(ResponseHelper.Success("删除成功"));
        }

        [HttpPost("ExecSql")]
        public IActionResult ExecSql() => StatusCode(403, ResponseHelper.Fail<object>("禁用"));
        #endregion
    }

    #region 模型
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
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
    }
    #endregion
}