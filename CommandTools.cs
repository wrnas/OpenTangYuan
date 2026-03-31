using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
                string savePath = $"D:\\screen_{DateTime.Now:yyyyMMddHHmmss}.png";
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

                // ✅ 返回纯路径，工作流才能正常使用！
                return savePath;
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
                string GetValue(string key, string defaultValue = "")
                {
                    if (args == null || !args.TryGetValue(key, out var value))
                        return defaultValue;
                    return value?.ToString()?.Trim() ?? defaultValue;
                }

                string toEmail = GetValue("to");
                string subject = GetValue("subject", "屏幕截图");
                string body = GetValue("body", "已为您截取屏幕");
                string attachment = GetValue("attachment");

                if (string.IsNullOrWhiteSpace(toEmail))
                    return "错误：收件人邮箱不能为空";

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("系统助手", "l00f@163.com"));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = subject;
                var builder = new BodyBuilder { TextBody = body };

                // ✅【超级严谨】三段过滤，绝不偷懒
                if (!string.IsNullOrWhiteSpace(attachment)      // 非空
                    && !attachment.Contains("{{")              // 不是模板变量
                    && File.Exists(attachment))                // 文件真实存在
                {
                    builder.Attachments.Add(attachment);
                }

                message.Body = builder.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync("smtp.163.com", 465, SecureSocketOptions.SslOnConnect);
                await client.AuthenticateAsync("l00f@163.com", "KYfLX3B7tj8tGE6A");
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                return $"邮件发送成功：{toEmail}";
            }
            catch (Exception ex)
            {
                return $"邮件发送失败：{ex.Message}";
            }
        }
        #endregion
    }


}