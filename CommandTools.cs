using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using TangYuan.Models;

namespace TangYuan.Tools
{
    /// <summary>
    /// 系统命令工具类
    /// 功能：锁屏、全屏截图、发送邮件（带附件）
    /// 纯静态类，供控制器直接调用
    /// </summary>
    public static class CommandTools
    {
        #region Win32 API 导入（系统底层调用，无需修改）
        // 锁屏 API
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        // 截图所需的系统 API
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int cx, int cy);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        // 系统常量：屏幕宽度、高度、复制屏幕图像标识
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int SRCCOPY = 0x00CC0020;
        #endregion

        #region 锁屏功能
        /// <summary>
        /// 调用系统 API 一键锁屏
        /// </summary>
        /// <returns>成功/失败提示</returns>
        public static async Task<object> LockScreenAsync()
        {
            await Task.CompletedTask;

            try
            {
                // 执行系统锁屏
                bool isSuccess = LockWorkStation();
                return isSuccess ? "✅ 锁屏成功" : "❌ 锁屏失败";
            }
            catch (Exception ex)
            {
                return $"❌ 锁屏异常：{ex.Message}";
            }
        }
        #endregion

        #region 全屏截图功能
        /// <summary>
        /// 全屏截图并保存到 D 盘根目录
        /// 文件名自动带时间戳，不会覆盖
        /// </summary>
        /// <returns>保存路径或失败信息</returns>
        public static async Task<object> CaptureScreenAsync()
        {
            IntPtr hdcSrc = IntPtr.Zero;
            IntPtr hdcDest = IntPtr.Zero;
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr hOldBitmap = IntPtr.Zero;

            try
            {
                string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshotImg");
                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);
                string savePath = Path.Combine(baseDir, $"screen_{DateTime.Now:yyyyMMddHHmmss}.png");

                int screenWidth = GetSystemMetrics(SM_CXSCREEN);
                int screenHeight = GetSystemMetrics(SM_CYSCREEN);

                hdcSrc = GetDC(IntPtr.Zero);
                hdcDest = CreateCompatibleDC(hdcSrc);
                hBitmap = CreateCompatibleBitmap(hdcSrc, screenWidth, screenHeight);
                hOldBitmap = SelectObject(hdcDest, hBitmap);

                BitBlt(hdcDest, 0, 0, screenWidth, screenHeight, hdcSrc, 0, 0, SRCCOPY);
                SelectObject(hdcDest, hOldBitmap);

                using var bmp = Image.FromHbitmap(hBitmap);
                bmp.Save(savePath, ImageFormat.Png);               

                return new SkillResult
                {
                    Success = true,
                    SkillCode = "screenshot_task",
                    Type = "screenshot",
                    Text = "截图成功",
                    Data = new
                    {
                        path = savePath,
                        fileName = Path.GetFileName(savePath)
                    }
                }.WithValue(savePath);
            }
            catch (Exception ex)
            {
                return $"截图失败：{ex.Message}";
            }
            finally
            {
                DeleteObject(hBitmap);
                DeleteDC(hdcDest);
                ReleaseDC(IntPtr.Zero, hdcSrc);
            }
        }
        #endregion

        #region 邮箱相关

        #region MailKit 发送邮件（带附件）
        /// <summary>
        /// 发送邮件（支持文本 + 附件）
        /// </summary>
        /// <param name="args">参数：to, subject, body, attachment</param>
        /// <param name="getString">控制器传过来的取值工具方法</param>
        /// <returns>发送结果</returns>
        /// <summary>
        /// MailKit 发送邮件（带附件）
        /// </summary>
        /// <param name="args">原始参数字典</param>
        /// <returns>发送结果</returns>
        // CommandTools.cs 里的 SendEmailAsync 改成这样
        // 发邮件 → 严谨判断：非空 + 不是模板 + 文件存在
        public static async Task<object> SendEmailAsync(Dictionary<string, object> args)
        {
            try
            {
                // ========== 读取配置 ==========
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                    .Build();

                var emailSection = config.GetSection("EmailSettings");
                string smtpServer = emailSection["SmtpServer"] ?? throw new Exception("配置缺失: SmtpServer");
                int smtpPort = int.Parse(emailSection["SmtpPort"] ?? "465");
                string senderEmail = emailSection["SenderEmail"] ?? throw new Exception("配置缺失: SenderEmail");
                string senderPassword = emailSection["SenderPassword"] ?? throw new Exception("配置缺失: SenderPassword");

                // ========== 原有辅助方法（不变） ==========
                string GetValue(string key, string defaultValue = "")
                {
                    if (args == null || !args.TryGetValue(key, out var value) || value == null)
                        return defaultValue;

                    return value switch
                    {
                        string s => s.Trim(),
                        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString()?.Trim() ?? defaultValue,
                        JsonElement je => je.ToString()?.Trim() ?? defaultValue,
                        _ => value.ToString()?.Trim() ?? defaultValue
                    };
                }

                List<string> GetStringList(string key)
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
                                if (item.ValueKind == JsonValueKind.String)
                                {
                                    var text = item.GetString()?.Trim();
                                    if (!string.IsNullOrWhiteSpace(text))
                                        result.Add(text);
                                }
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

                    if (value is string s)
                    {
                        if (!string.IsNullOrWhiteSpace(s))
                            result.Add(s.Trim());
                        return result;
                    }

                    if (value is IEnumerable<object> list)
                    {
                        foreach (var item in list)
                        {
                            var text = item?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                                result.Add(text);
                        }
                        return result;
                    }

                    var single = value.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(single))
                        result.Add(single);

                    return result;
                }

                List<string> SplitEmails(string input)
                {
                    return (input ?? "")
                        .Split(new[] { ',', ';', '，', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                // ========== 业务逻辑（使用配置中的发件人） ==========
                string toRaw = GetValue("to");
                string subject = GetValue("subject", "系统邮件");
                string body = GetValue("body");
                if (string.IsNullOrWhiteSpace(body))
                    body = GetValue("content", "");

                var toList = SplitEmails(toRaw);
                if (toList.Count == 0)
                    throw new ArgumentException("收件人邮箱不能为空");

                var attachmentList = new List<string>();
                string singleAttachment = GetValue("attachment");
                if (!string.IsNullOrWhiteSpace(singleAttachment))
                    attachmentList.Add(singleAttachment);

                var attachments = GetStringList("attachments");
                foreach (var item in attachments)
                    if (!string.IsNullOrWhiteSpace(item))
                        attachmentList.Add(item);

                attachmentList = attachmentList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("系统助手", senderEmail));   // ✅ 从配置读取

                foreach (var email in toList)
                    message.To.Add(MailboxAddress.Parse(email));

                message.Subject = subject;

                var builder = new BodyBuilder { TextBody = body };
                var attachedFiles = new List<string>();
                var missingFiles = new List<string>();

                foreach (var file in attachmentList)
                {
                    if (System.IO.File.Exists(file))
                    {
                        builder.Attachments.Add(file);
                        attachedFiles.Add(file);
                    }
                    else
                    {
                        missingFiles.Add(file);
                    }
                }

                message.Body = builder.ToMessageBody();

                using var client = new MailKit.Net.Smtp.SmtpClient();
                await client.ConnectAsync(smtpServer, smtpPort, MailKit.Security.SecureSocketOptions.SslOnConnect);
                await client.AuthenticateAsync(senderEmail, senderPassword);    // ✅ 从配置读取
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                bool attachmentAdded = attachedFiles.Count > 0;
                return new SkillResult
                {
                    Success = true,
                    SkillCode = "email_task",
                    Type = "send_email",
                    Text = attachmentAdded ? "邮件发送成功（含附件）" : "邮件发送成功",
                    Data = new
                    {
                        to = toList,
                        subject,
                        body,
                        attachments = attachedFiles,
                        missingAttachments = missingFiles,
                        attachmentAdded
                    }
                }.Normalize();
            }
            catch (Exception ex)
            {
                throw new Exception("邮件发送失败：" + ex.Message, ex);
            }
        }


        #endregion

        #endregion
    }


}