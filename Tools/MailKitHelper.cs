using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Configuration;
using MimeKit;

/// <summary>
/// MailKit 邮件通用操作封装（静态类，从 appsettings.json 读取配置）
/// </summary>
public static class MailKitHelper
{
    #region 配置加载（私有）

    private static IConfigurationRoot _configuration;
    private static readonly object _lock = new object();

    private static IConfigurationRoot Configuration
    {
        get
        {
            if (_configuration == null)
            {
                lock (_lock)
                {
                    if (_configuration == null)
                    {
                        _configuration = new ConfigurationBuilder()
                            .SetBasePath(Directory.GetCurrentDirectory())
                            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                            .Build();
                    }
                }
            }
            return _configuration;
        }
    }

    private static string GetEmailSetting(string key)
    {
        return Configuration.GetSection("EmailSettings")[key] ?? throw new Exception($"配置缺失: EmailSettings:{key}");
    }

    private static int GetEmailSettingInt(string key)
    {
        return int.Parse(GetEmailSetting(key));
    }

    private static bool GetEmailSettingBool(string key)
    {
        return bool.Parse(GetEmailSetting(key));
    }

    #endregion

    #region 辅助方法：从 Dictionary<string, object> 取值（兼容各种类型）

    private static string GetString(Dictionary<string, object> args, string key, string defaultValue = "")
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

    private static List<string> GetStringList(Dictionary<string, object> args, string key)
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

    private static List<string> SplitEmails(string input)
    {
        return (input ?? "")
            .Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool GetBool(Dictionary<string, object> args, string key, bool defaultValue = false)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return defaultValue;
        if (value is bool b) return b;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (value is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(value.ToString(), out var parsed))
            return parsed;
        return defaultValue;
    }

    private static int GetInt(Dictionary<string, object> args, string key, int defaultValue = 0)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return defaultValue;
        if (value is int i) return i;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Number)
            return je.GetInt32();
        if (int.TryParse(value.ToString(), out var parsed))
            return parsed;
        return defaultValue;
    }

    #endregion

    #region 核心：发送邮件（固定签名，不可变）

    /// <summary>
    /// 发送邮件（支持文本、HTML、附件）- 参数格式固定，兼容原有调用
    /// </summary>
    /// <param name="args">参数字典，支持键：to, subject, body, attachment, attachments, cc, bcc, isBodyHtml</param>
    /// <returns>SkillResult 格式的对象</returns>
    public static async Task<object> SendEmailAsync(Dictionary<string, object> args)
    {
        try
        {
            // 读取配置
            string smtpServer = GetEmailSetting("SmtpServer");
            int smtpPort = GetEmailSettingInt("SmtpPort");
            bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
            string senderEmail = GetEmailSetting("SenderEmail");
            string senderPassword = GetEmailSetting("SenderPassword");

            // 提取参数
            string toRaw = GetString(args, "to");
            string subject = GetString(args, "subject", "系统邮件");
            string body = GetString(args, "body");
            if (string.IsNullOrWhiteSpace(body))
                body = GetString(args, "content", "");

            bool isBodyHtml = GetBool(args, "isBodyHtml", true);
            var toList = SplitEmails(toRaw);
            if (toList.Count == 0)
                throw new ArgumentException("收件人邮箱不能为空");

            var ccRaw = GetString(args, "cc");
            var ccList = SplitEmails(ccRaw);
            var bccRaw = GetString(args, "bcc");
            var bccList = SplitEmails(bccRaw);

            // 附件列表
            var attachmentList = new List<string>();
            string singleAttachment = GetString(args, "attachment");
            if (!string.IsNullOrWhiteSpace(singleAttachment))
                attachmentList.Add(singleAttachment);
            var attachments = GetStringList(args, "attachments");
            attachmentList.AddRange(attachments);
            attachmentList = attachmentList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // 构建邮件
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("", senderEmail));
            foreach (var email in toList)
                message.To.Add(MailboxAddress.Parse(email));
            foreach (var email in ccList)
                message.Cc.Add(MailboxAddress.Parse(email));
            foreach (var email in bccList)
                message.Bcc.Add(MailboxAddress.Parse(email));
            message.Subject = subject;

            var builder = new BodyBuilder();
            if (isBodyHtml)
                builder.HtmlBody = body;
            else
                builder.TextBody = body;

            var attachedFiles = new List<string>();
            var missingFiles = new List<string>();
            foreach (var file in attachmentList)
            {
                if (File.Exists(file))
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

            // 发送
            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, smtpUseSsl);
            await client.AuthenticateAsync(senderEmail, senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return new
            {
                Success = true,
                SkillCode = "email_task",
                Type = "send_email",
                Text = attachedFiles.Count > 0 ? "邮件发送成功（含附件）" : "邮件发送成功",
                Data = new
                {
                    to = toList,
                    cc = ccList,
                    bcc = bccList,
                    subject,
                    body,
                    attachments = attachedFiles,
                    missingAttachments = missingFiles
                }
            };
        }
        catch (Exception ex)
        {
            throw new Exception("邮件发送失败：" + ex.Message, ex);
        }
    }

    #endregion

    #region 私有 IMAP 连接辅助

    private static async Task<ImapClient> ConnectImapAsync(CancellationToken cancellationToken = default)
    {
        string imapServer = GetEmailSetting("ImapServer");
        int imapPort = GetEmailSettingInt("ImapPort");
        bool imapUseSsl = GetEmailSettingBool("ImapUseSsl");
        string account = GetEmailSetting("SenderEmail");
        string password = GetEmailSetting("SenderPassword");

        var client = new ImapClient();
        await client.ConnectAsync(imapServer, imapPort, imapUseSsl, cancellationToken);
        await client.AuthenticateAsync(account, password, cancellationToken);
        return client;
    }

    #endregion

    #region 公开 IMAP 操作方法

    /// <summary>
    /// 获取未读邮件摘要（高性能，只含基本元数据）
    /// </summary>
    /// <param name="maxCount">最大返回数量，默认20</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>邮件摘要列表</returns>
    public static async Task<List<IMessageSummary>> GetUnreadSummariesAsync(int maxCount = 20, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uids = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
        var takeUids = uids.Take(maxCount).ToArray();
        if (!takeUids.Any())
            return new List<IMessageSummary>();

        var summaries = await inbox.FetchAsync(takeUids,
            MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
            MessageSummaryItems.InternalDate | MessageSummaryItems.UniqueId |
            MessageSummaryItems.BodyStructure, cancellationToken);
        return summaries.ToList();
    }

    /// <summary>
    /// 根据搜索条件获取邮件摘要（高性能）
    /// </summary>
    /// <param name="query">SearchQuery 条件</param>
    /// <param name="maxCount">最大返回数量</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<List<IMessageSummary>> SearchSummariesAsync(SearchQuery query, int maxCount = 50, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uids = await inbox.SearchAsync(query, cancellationToken);
        var takeUids = uids.Take(maxCount).ToArray();
        if (!takeUids.Any())
            return new List<IMessageSummary>();

        var summaries = await inbox.FetchAsync(takeUids,
            MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
            MessageSummaryItems.InternalDate | MessageSummaryItems.UniqueId |
            MessageSummaryItems.BodyStructure, cancellationToken);
        return summaries.ToList();
    }

    /// <summary>
    /// 根据 UID 获取完整邮件对象
    /// </summary>
    public static async Task<MimeMessage> GetMessageByUidAsync(UniqueId uid, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        return await inbox.GetMessageAsync(uid, cancellationToken);
    }

    /// <summary>
    /// 下载指定邮件的所有附件
    /// </summary>
    /// <param name="uid">邮件UID</param>
    /// <param name="savePath">保存目录</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载的文件路径列表</returns>
    public static async Task<List<string>> DownloadAttachmentsAsync(UniqueId uid, string savePath, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var message = await inbox.GetMessageAsync(uid, cancellationToken);
        Directory.CreateDirectory(savePath);
        var downloaded = new List<string>();

        foreach (var attachment in message.Attachments)
        {
            if (attachment is MimePart part && part.IsAttachment)
            {
                string fileName = part.FileName ?? "unnamed.bin";
                string safeName = SanitizeFileName(fileName);
                string filePath = Path.Combine(savePath, safeName);
                filePath = GetUniqueFilePath(filePath);

                using var stream = File.Create(filePath);
                await part.Content.DecodeToAsync(stream, cancellationToken);
                downloaded.Add(filePath);
            }
        }
        return downloaded;
    }

    /// <summary>
    /// 标记邮件为已读
    /// </summary>
    public static async Task MarkAsReadAsync(UniqueId uid, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
    }

    /// <summary>
    /// 回复邮件（支持纯文本或HTML，可带附件）
    /// </summary>
    /// <param name="originalUid">原始邮件UID</param>
    /// <param name="replyText">回复文本（纯文本）</param>
    /// <param name="replyHtml">回复HTML（可选，若提供则优先使用HTML格式）</param>
    /// <param name="replyToAll">是否回复所有人</param>
    /// <param name="attachments">附件路径列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task ReplyToEmailAsync(UniqueId originalUid, string replyText, string replyHtml = null,
        bool replyToAll = false, List<string> attachments = null, CancellationToken cancellationToken = default)
    {
        // 获取原始邮件
        var original = await GetMessageByUidAsync(originalUid, cancellationToken);
        if (original == null)
            throw new ArgumentException("未找到原始邮件");

        // 构建回复
        var reply = new MimeMessage();
        string senderEmail = GetEmailSetting("SenderEmail");
        string smtpServer = GetEmailSetting("SmtpServer");
        int smtpPort = GetEmailSettingInt("SmtpPort");
        bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
        string senderPassword = GetEmailSetting("SenderPassword");

        reply.From.Add(new MailboxAddress("", senderEmail));

        // 收件人
        if (original.ReplyTo.Count > 0)
            reply.To.AddRange(original.ReplyTo);
        else if (original.From.Count > 0)
            reply.To.AddRange(original.From);
        else if (original.Sender != null)
            reply.To.Add(original.Sender);

        if (replyToAll)
        {
            foreach (var m in original.To.Mailboxes.Where(m => m.Address != senderEmail))
                reply.To.Add(m);
            foreach (var m in original.Cc.Mailboxes.Where(m => m.Address != senderEmail))
                reply.Cc.Add(m);
        }

        // 主题
        if (!original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase))
            reply.Subject = "Re: " + original.Subject;
        else
            reply.Subject = original.Subject;

        // In-Reply-To / References
        if (!string.IsNullOrEmpty(original.MessageId))
        {
            reply.InReplyTo = original.MessageId;
            foreach (var id in original.References)
                reply.References.Add(id);
            reply.References.Add(original.MessageId);
        }

        // 引用原文
        var builder = new BodyBuilder();
        string quotedPlain = BuildQuotedText(original);
        string quotedHtml = BuildQuotedHtml(original);

        if (!string.IsNullOrEmpty(replyHtml))
        {
            builder.HtmlBody = $"<div>{replyHtml}</div><br/>{quotedHtml}";
            builder.TextBody = $"{replyText}\n\n{quotedPlain}";
        }
        else
        {
            builder.TextBody = $"{replyText}\n\n{quotedPlain}";
        }

        if (attachments != null)
        {
            foreach (var file in attachments.Where(File.Exists))
                builder.Attachments.Add(file);
        }

        reply.Body = builder.ToMessageBody();

        // 发送
        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(smtpServer, smtpPort, smtpUseSsl, cancellationToken);
        await smtp.AuthenticateAsync(senderEmail, senderPassword, cancellationToken);
        await smtp.SendAsync(reply, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    /// <summary>
    /// 保存邮件为 .eml 文件
    /// </summary>
    public static async Task SaveToEmlAsync(UniqueId uid, string filePath, CancellationToken cancellationToken = default)
    {
        var message = await GetMessageByUidAsync(uid, cancellationToken);
        using var stream = File.Create(filePath);
        await message.WriteToAsync(stream, cancellationToken);
    }

    #endregion

    #region 插入图片
    /// <summary>
    /// 发送 HTML 邮件，并将本地图片嵌入到正文中（不显示为附件）
    /// </summary>
    /// <param name="args">参数字典，支持键：
    ///   - to: 收件人（必填）
    ///   - subject: 主题（必填）
    ///   - htmlBody: HTML 正文，图片占位符使用 &lt;img src="cid:图片标识"&gt;
    ///   - embeddedImages: 字典，键为图片标识（cid），值为本地图片路径，例如 { "image1", @"c:\a.png" }
    ///   - cc, bcc, attachments（普通附件）可选
    /// </param>
    public static async Task<object> SendHtmlWithEmbeddedImageAsync(Dictionary<string, object> args)
    {
        try
        {
            // 读取配置（与 SendEmailAsync 相同）
            string smtpServer = GetEmailSetting("SmtpServer");
            int smtpPort = GetEmailSettingInt("SmtpPort");
            bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
            string senderEmail = GetEmailSetting("SenderEmail");
            string senderPassword = GetEmailSetting("SenderPassword");

            // 提取参数
            string toRaw = GetString(args, "to");
            string subject = GetString(args, "subject", "系统邮件");
            string htmlBody = GetString(args, "htmlBody");
            if (string.IsNullOrWhiteSpace(htmlBody))
                throw new ArgumentException("htmlBody 不能为空");

            var toList = SplitEmails(toRaw);
            if (toList.Count == 0)
                throw new ArgumentException("收件人不能为空");

            var ccList = SplitEmails(GetString(args, "cc"));
            var bccList = SplitEmails(GetString(args, "bcc"));

            // 获取内嵌图片字典（键: cid, 值: 本地路径）
            var embeddedImages = new Dictionary<string, string>();
            if (args.TryGetValue("embeddedImages", out var embValue) && embValue != null)
            {
                if (embValue is Dictionary<string, string> dict)
                    embeddedImages = dict;
                else if (embValue is JsonElement je && je.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in je.EnumerateObject())
                    {
                        string path = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(path))
                            embeddedImages[prop.Name] = path;
                    }
                }
            }

            // 普通附件
            var attachmentList = new List<string>();
            string singleAttachment = GetString(args, "attachment");
            if (!string.IsNullOrWhiteSpace(singleAttachment))
                attachmentList.Add(singleAttachment);
            attachmentList.AddRange(GetStringList(args, "attachments"));
            attachmentList = attachmentList.Distinct().ToList();

            // 构建邮件
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("", senderEmail));
            foreach (var email in toList) message.To.Add(MailboxAddress.Parse(email));
            foreach (var email in ccList) message.Cc.Add(MailboxAddress.Parse(email));
            foreach (var email in bccList) message.Bcc.Add(MailboxAddress.Parse(email));
            message.Subject = subject;

            var builder = new BodyBuilder();

            // 处理内嵌图片
            foreach (var (cid, imagePath) in embeddedImages)
            {
                if (File.Exists(imagePath))
                {
                    var image = builder.LinkedResources.Add(imagePath);
                    image.ContentId = cid;
                }
                else
                {
                    throw new FileNotFoundException($"内嵌图片不存在: {imagePath}");
                }
            }

            // 设置 HTML 正文（其中的 src="cid:xxx" 会指向上面添加的资源）
            builder.HtmlBody = htmlBody;

            // 添加普通附件
            var attachedFiles = new List<string>();
            var missingFiles = new List<string>();
            foreach (var file in attachmentList)
            {
                if (File.Exists(file))
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

            // 发送
            using var client = new SmtpClient();
            await client.ConnectAsync(smtpServer, smtpPort, smtpUseSsl);
            await client.AuthenticateAsync(senderEmail, senderPassword);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            return new
            {
                Success = true,
                SkillCode = "email_task",
                Type = "send_html_with_embedded_image",
                Text = "邮件发送成功（含内嵌图片）",
                Data = new
                {
                    to = toList,
                    cc = ccList,
                    bcc = bccList,
                    subject,
                    embeddedImages = embeddedImages.Keys.ToList(),
                    attachments = attachedFiles,
                    missingAttachments = missingFiles
                }
            };
        }
        catch (Exception ex)
        {
            throw new Exception("发送内嵌图片邮件失败：" + ex.Message, ex);
        }
    }
    #endregion

    #region 私有辅助方法（文件名处理、引用原文）

    private static string SanitizeFileName(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName;
    }

    private static string GetUniqueFilePath(string filePath)
    {
        if (!File.Exists(filePath))
            return filePath;

        string dir = Path.GetDirectoryName(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath);
        int counter = 1;
        string newPath;
        do
        {
            newPath = Path.Combine(dir, $"{name}_{counter}{ext}");
            counter++;
        } while (File.Exists(newPath));
        return newPath;
    }

    private static string BuildQuotedText(MimeMessage original)
    {
        string plain = original.TextBody ?? "（无文本内容）";
        return $@"
-------- 原始邮件 --------
主题：{original.Subject}
发件人：{original.From}
时间：{original.Date:yyyy-MM-dd HH:mm:ss}

{plain}";
    }

    private static string BuildQuotedHtml(MimeMessage original)
    {
        string html = original.HtmlBody ?? "<p>（无HTML内容）</p>";
        return $@"
<br><hr>
<p><strong>原始邮件：</strong></p>
<p>主题：{original.Subject}<br>
发件人：{original.From}<br>
时间：{original.Date:yyyy-MM-dd HH:mm:ss}</p>
{html}";
    }

    #endregion
}