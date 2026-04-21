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
/// 包含：发送邮件（支持内嵌图片）、接收、搜索、附件下载、回复、标记、移动、删除等
/// </summary>
public static class MailKitHelper
{
    #region 配置加载

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
        return Configuration.GetSection("EmailSettings")[key]
               ?? throw new Exception($"配置缺失: EmailSettings:{key}");
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

    #region 辅助方法：从 Dictionary<string, object> 取值

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

    #endregion

    #region 发送邮件（唯一入口，支持内嵌图片）

    /// <summary>
    /// 发送邮件（支持纯文本、HTML、普通附件、内嵌图片）
    /// </summary>
    /// <param name="args">参数字典，支持以下键：
    ///   - to: 收件人，多个用逗号/分号/空格分隔（必填）
    ///   - subject: 主题（必填）
    ///   - body: 正文（纯文本或HTML，取决于 isBodyHtml）
    ///   - isBodyHtml: 是否HTML格式，默认 true（当使用内嵌图片时忽略此参数，强制HTML）
    ///   - cc: 抄送（可选）
    ///   - bcc: 密送（可选）
    ///   - attachment / attachments: 附件路径（可选）
    ///   - embeddedImages: 内嵌图片字典，键为 cid，值为本地图片路径（可选，提供此键则自动使用HTML模式）
    ///   - htmlBody: 当使用内嵌图片时，HTML正文（可选，若未提供则使用 body）
    /// </param>
    public static async Task<object> SendEmailAsync(Dictionary<string, object> args)
    {
        // 读取配置
        string smtpServer = GetEmailSetting("SmtpServer");
        int smtpPort = GetEmailSettingInt("SmtpPort");
        bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
        string senderEmail = GetEmailSetting("SenderEmail");
        string senderPassword = GetEmailSetting("SenderPassword");

        // 解析基础参数
        string toRaw = GetString(args, "to");
        string subject = GetString(args, "subject", "系统邮件");
        if (string.IsNullOrWhiteSpace(toRaw))
            throw new ArgumentException("收件人邮箱不能为空");

        var toList = SplitEmails(toRaw);
        var ccList = SplitEmails(GetString(args, "cc"));
        var bccList = SplitEmails(GetString(args, "bcc"));

        // 附件列表
        var attachmentList = new List<string>();
        string singleAttachment = GetString(args, "attachment");
        if (!string.IsNullOrWhiteSpace(singleAttachment))
            attachmentList.Add(singleAttachment);
        attachmentList.AddRange(GetStringList(args, "attachments"));
        attachmentList = attachmentList.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 检测是否需要内嵌图片
        bool hasEmbeddedImages = args.TryGetValue("embeddedImages", out var embValue) && embValue != null;

        // 构建邮件
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("", senderEmail));
        foreach (var email in toList) message.To.Add(MailboxAddress.Parse(email));
        foreach (var email in ccList) message.Cc.Add(MailboxAddress.Parse(email));
        foreach (var email in bccList) message.Bcc.Add(MailboxAddress.Parse(email));
        message.Subject = subject;

        var builder = new BodyBuilder();
        var attachedFiles = new List<string>();
        var missingFiles = new List<string>();

        // 添加普通附件
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

        if (hasEmbeddedImages)
        {
            // 解析内嵌图片字典
            var embeddedImages = new Dictionary<string, string>();
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

            // 添加内嵌图片资源
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

            // 获取 HTML 正文：优先 htmlBody，否则使用 body
            string htmlBody = GetString(args, "htmlBody");
            if (string.IsNullOrEmpty(htmlBody))
                htmlBody = GetString(args, "body", "");
            if (string.IsNullOrEmpty(htmlBody))
                throw new ArgumentException("使用内嵌图片时必须提供 htmlBody 或 body 作为 HTML 内容");
            builder.HtmlBody = htmlBody;
        }
        else
        {
            // 普通模式：根据 isBodyHtml 决定
            bool isBodyHtml = GetBool(args, "isBodyHtml", true);
            string body = GetString(args, "body", "");
            if (isBodyHtml)
                builder.HtmlBody = body;
            else
                builder.TextBody = body;
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
            Text = hasEmbeddedImages ? "邮件发送成功（含内嵌图片）" : (attachedFiles.Count > 0 ? "邮件发送成功（含附件）" : "邮件发送成功"),
            Data = new
            {
                to = toList,
                cc = ccList,
                bcc = bccList,
                subject,
                attachments = attachedFiles,
                missingAttachments = missingFiles,
                hasEmbeddedImages
            }
        };
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

    #region IMAP 操作：获取邮件摘要（高性能）

    /// <summary>
    /// 获取未读邮件摘要（高性能，只含基本元数据）
    /// </summary>
    /// <param name="maxCount">最大返回数量，默认20</param>
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
    /// <param name="query">SearchQuery 条件（如 SearchQuery.SubjectContains("2026").And(SearchQuery.NotSeen)）</param>
    /// <param name="maxCount">最大返回数量</param>
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

    #endregion

    #region IMAP 操作：获取完整邮件

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

    #endregion

    #region IMAP 操作：下载附件

    /// <summary>
    /// 下载指定邮件的所有附件
    /// </summary>
    /// <param name="uid">邮件UID</param>
    /// <param name="savePath">保存目录</param>
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

    #endregion

    #region IMAP 操作：标记已读、移动、删除

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

    

    

    #endregion

    #region IMAP 操作：回复邮件

    /// <summary>
    /// 回复邮件（支持纯文本或HTML，可带附件）
    /// </summary>
    /// <param name="originalUid">原始邮件UID</param>
    /// <param name="replyText">回复文本（纯文本）</param>
    /// <param name="replyHtml">回复HTML（可选，若提供则优先使用HTML格式）</param>
    /// <param name="replyToAll">是否回复所有人</param>
    /// <param name="attachments">附件路径列表</param>
    public static async Task ReplyToEmailAsync(UniqueId originalUid, string replyText, string replyHtml = null,
        bool replyToAll = false, List<string> attachments = null, CancellationToken cancellationToken = default)
    {
        var original = await GetMessageByUidAsync(originalUid, cancellationToken);
        if (original == null)
            throw new ArgumentException("未找到原始邮件");

        string senderEmail = GetEmailSetting("SenderEmail");
        string smtpServer = GetEmailSetting("SmtpServer");
        int smtpPort = GetEmailSettingInt("SmtpPort");
        bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
        string senderPassword = GetEmailSetting("SenderPassword");

        var reply = new MimeMessage();
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

        // 构建正文并引用原文
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

    #endregion

    #region IMAP 操作：保存为 .eml 文件

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