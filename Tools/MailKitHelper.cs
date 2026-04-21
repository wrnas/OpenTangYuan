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
using MailKit.Security;
using System.Text;


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
                            .SetBasePath(AppContext.BaseDirectory)
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
    /// <summary>
    /// 发送邮件（支持纯文本、HTML、普通附件、内嵌图片）
    /// 兼容两种模式：
    /// 1. 传统模式：embeddedImages + htmlBody
    /// 2. AI友好模式：insertImagePaths 自动插入正文
    /// </summary>
    public static async Task<object> SendEmailAsync(Dictionary<string, object> args)
    {
        string smtpServer = GetEmailSetting("SmtpServer");
        int smtpPort = GetEmailSettingInt("SmtpPort");
        bool smtpUseSsl = GetEmailSettingBool("SmtpUseSsl");
        string senderEmail = GetEmailSetting("SenderEmail");
        string senderPassword = GetEmailSetting("SenderPassword");

        string toRaw = GetString(args, "to");
        string subject = GetString(args, "subject", "系统邮件");
        if (string.IsNullOrWhiteSpace(toRaw))
            throw new ArgumentException("收件人邮箱不能为空");

        var toList = SplitEmails(toRaw);
        var ccList = SplitEmails(GetString(args, "cc"));
        var bccList = SplitEmails(GetString(args, "bcc"));

        var attachmentList = new List<string>();
        string singleAttachment = GetString(args, "attachment");
        if (!string.IsNullOrWhiteSpace(singleAttachment))
            attachmentList.Add(singleAttachment);
        attachmentList.AddRange(GetStringList(args, "attachments"));
        attachmentList = attachmentList
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var insertImagePaths = GetStringList(args, "insertImagePaths")
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasEmbeddedImages = args.TryGetValue("embeddedImages", out var embValue) && embValue != null;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("", senderEmail));
        foreach (var email in toList) message.To.Add(MailboxAddress.Parse(email));
        foreach (var email in ccList) message.Cc.Add(MailboxAddress.Parse(email));
        foreach (var email in bccList) message.Bcc.Add(MailboxAddress.Parse(email));
        message.Subject = subject;

        var builder = new BodyBuilder();
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

        // 方案 A：兼容旧版 embeddedImages
        if (hasEmbeddedImages)
        {
            var embeddedImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (embValue is Dictionary<string, string> dict)
            {
                embeddedImages = dict;
            }
            else if (embValue is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    string path = prop.Value.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(path))
                        embeddedImages[prop.Name] = path;
                }
            }

            foreach (var kv in embeddedImages)
            {
                var cid = kv.Key;
                var imagePath = kv.Value;

                if (!File.Exists(imagePath))
                    throw new FileNotFoundException($"内嵌图片不存在: {imagePath}");

                var image = builder.LinkedResources.Add(imagePath);
                image.ContentId = cid;
            }

            string htmlBody = GetString(args, "htmlBody");
            if (string.IsNullOrWhiteSpace(htmlBody))
                htmlBody = GetString(args, "body", "");

            if (string.IsNullOrWhiteSpace(htmlBody))
                throw new ArgumentException("使用内嵌图片时必须提供 htmlBody 或 body 作为 HTML 内容");

            builder.HtmlBody = htmlBody;
        }
        else
        {
            // 方案 B：AI友好模式 insertImagePaths
            bool isBodyHtml = GetBool(args, "isBodyHtml", true);
            string body = GetString(args, "body", "");

            if (insertImagePaths.Count > 0)
            {
                var sb = new StringBuilder();
                if (isBodyHtml)
                {
                    sb.Append(string.IsNullOrWhiteSpace(body) ? "" : body);
                }
                else
                {
                    sb.Append(System.Net.WebUtility.HtmlEncode(body).Replace("\r\n", "<br/>").Replace("\n", "<br/>"));
                }

                int idx = 1;
                foreach (var imgPath in insertImagePaths)
                {
                    var image = builder.LinkedResources.Add(imgPath);
                    image.ContentId = $"img{idx}";
                    if (sb.Length > 0) sb.Append("<br/>");
                    sb.Append($"<img src=\"cid:img{idx}\" />");
                    idx++;
                }

                builder.HtmlBody = sb.ToString();
                if (!string.IsNullOrWhiteSpace(body))
                    builder.TextBody = body;
            }
            else
            {
                if (isBodyHtml)
                    builder.HtmlBody = body;
                else
                    builder.TextBody = body;
            }
        }

        message.Body = builder.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtpServer, smtpPort, smtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
        await client.AuthenticateAsync(senderEmail, senderPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        return new
        {
            Success = true,
            SkillCode = "email_task",
            Type = "send_email",
            Text = insertImagePaths.Count > 0
                ? "邮件发送成功（正文含图片）"
                : (hasEmbeddedImages
                    ? "邮件发送成功（含内嵌图片）"
                    : (attachedFiles.Count > 0 ? "邮件发送成功（含附件）" : "邮件发送成功")),
            Data = new
            {
                to = toList,
                cc = ccList,
                bcc = bccList,
                subject,
                attachments = attachedFiles,
                missingAttachments = missingFiles,
                insertImagePaths,
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

        await client.ConnectAsync(
            imapServer,
            imapPort,
            imapUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);

        await client.AuthenticateAsync(account, password, cancellationToken);

        // 解决网易邮箱 "EXAMINE Unsafe Login" 问题
        try
        {
            var clientImplementation = new ImapImplementation
            {
                Name = "TangYuan",
                Version = "1.0.0",
                Vendor = "TangYuan",
            };
            await client.IdentifyAsync(clientImplementation, cancellationToken);
        }
        catch
        {
            // 某些 IMAP 服务器不支持 ID 命令，忽略即可
        }

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
            string fileName = GetAttachmentFileName(attachment);
            string safeName = SanitizeFileName(fileName);
            string filePath = Path.Combine(savePath, safeName);
            filePath = GetUniqueFilePath(filePath);

            switch (attachment)
            {
                case MimePart part:
                    using (var stream = File.Create(filePath))
                    {
                        await part.Content.DecodeToAsync(stream, cancellationToken);
                    }
                    downloaded.Add(filePath);
                    break;

                case MessagePart messagePart:
                    using (var stream = File.Create(filePath))
                    {
                        await messagePart.Message.WriteToAsync(stream, cancellationToken);
                    }
                    downloaded.Add(filePath);
                    break;
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


    #region AI友好封装

    public static string BuildMailRef(UniqueId uid) => $"INBOX:{uid.Id}";

    public static bool TryParseMailRef(string mailRef, out UniqueId uid)
    {
        uid = UniqueId.Invalid;

        if (string.IsNullOrWhiteSpace(mailRef))
            return false;

        var parts = mailRef.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        if (!uint.TryParse(parts[1], out var id))
            return false;

        uid = new UniqueId(id);
        return uid.IsValid;
    }

    public static async Task<List<EmailListItemDto>> SearchEmailsForAiAsync(
    string subjectKeyword = "",
    string fromKeyword = "",
    string bodyKeyword = "",
    bool unreadOnly = false,
    bool hasAttachments = false,
    int maxCount = 10,
    CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        // 服务器端只做最宽松的查询，避免 163 的 SEARCH 不稳定
        SearchQuery query = SearchQuery.All;

        if (!string.IsNullOrWhiteSpace(fromKeyword))
            query = query.And(SearchQuery.FromContains(fromKeyword));

        var uids = await inbox.SearchAsync(query, cancellationToken);

        // 多取一些，后面本地过滤
        int fetchCount = Math.Max(maxCount * 10, 100);
        var orderedUids = uids.Reverse().Take(fetchCount).ToArray();

        if (orderedUids.Length == 0)
            return new List<EmailListItemDto>();

        var summaries = await inbox.FetchAsync(
            orderedUids,
            MessageSummaryItems.Envelope |
            MessageSummaryItems.Flags |
            MessageSummaryItems.InternalDate |
            MessageSummaryItems.UniqueId,
            cancellationToken);

        var items = new List<EmailListItemDto>();

        foreach (var summary in summaries.OrderByDescending(x => x.InternalDate ?? DateTimeOffset.MinValue))
        {
            var message = await inbox.GetMessageAsync(summary.UniqueId, cancellationToken);

            string subject = message.Subject ?? "";
            string textBody = message.TextBody ?? "";
            string htmlBody = message.HtmlBody ?? "";
            string mergedBody = !string.IsNullOrWhiteSpace(textBody) ? textBody : StripHtmlToText(htmlBody);

            bool isUnread = summary.Flags == null || !summary.Flags.Value.HasFlag(MessageFlags.Seen);
            bool realHasAttachments = message.Attachments.Any();

            if (unreadOnly && !isUnread)
                continue;

            if (!string.IsNullOrWhiteSpace(subjectKeyword) &&
                subject.IndexOf(subjectKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (!string.IsNullOrWhiteSpace(bodyKeyword) &&
                mergedBody.IndexOf(bodyKeyword, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (hasAttachments && !realHasAttachments)
                continue;

            items.Add(new EmailListItemDto
            {
                Index = items.Count + 1,
                MailRef = BuildMailRef(summary.UniqueId),
                Uid = summary.UniqueId.Id.ToString(),
                Subject = string.IsNullOrWhiteSpace(subject) ? "(无主题)" : subject,
                From = string.Join("; ", summary.Envelope?.From?.Select(f => f.ToString()) ?? Enumerable.Empty<string>()),
                DateText = (summary.InternalDate?.LocalDateTime ?? DateTime.MinValue).ToString("yyyy-MM-dd HH:mm:ss"),
                IsUnread = isUnread,
                HasAttachments = realHasAttachments
            });

            if (items.Count >= maxCount)
                break;
        }

        return items;
    }




    public static async Task<EmailDetailDto> ReadEmailForAiAsync(UniqueId uid, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var message = await inbox.GetMessageAsync(uid, cancellationToken);

        var summaries = await inbox.FetchAsync(
            new[] { uid },
            MessageSummaryItems.Flags |
            MessageSummaryItems.UniqueId |
            MessageSummaryItems.Envelope |
            MessageSummaryItems.InternalDate,
            cancellationToken);

        var summary = summaries.FirstOrDefault();

        var attachmentNames = new List<string>();
        foreach (var attachment in message.Attachments)
        {
            attachmentNames.Add(GetAttachmentFileName(attachment));
        }

        string textBody = message.TextBody ?? "";
        string htmlBody = message.HtmlBody ?? "";
        string htmlPreview = StripHtmlToText(htmlBody);

        return new EmailDetailDto
        {
            MailRef = BuildMailRef(uid),
            Uid = uid.Id.ToString(),
            Subject = message.Subject ?? "(无主题)",
            From = string.Join("; ", message.From.Select(x => x.ToString())),
            To = string.Join("; ", message.To.Select(x => x.ToString())),
            Cc = string.Join("; ", message.Cc.Select(x => x.ToString())),
            DateText = message.Date.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
            HasAttachments = attachmentNames.Count > 0,
            IsUnread = summary?.Flags == null || !summary.Flags.Value.HasFlag(MessageFlags.Seen),
            TextBody = textBody,
            HtmlBody = htmlBody,
            HtmlPreview = htmlPreview,
            Attachments = attachmentNames
        };
    }


    

    private static string GetAttachmentFileName(MimeEntity entity)
    {
        if (entity is MimePart part)
        {
            return part.FileName
                   ?? part.ContentDisposition?.FileName
                   ?? part.ContentType?.Name
                   ?? "unnamed.bin";
        }

        if (entity is MessagePart messagePart)
        {
            return messagePart.ContentDisposition?.FileName
                   ?? messagePart.ContentType?.Name
                   ?? "attached-message.eml";
        }

        return "attachment.bin";
    }

    private static string StripHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        string text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "</p>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }





    #endregion

}

public class EmailListItemDto
{
    public int Index { get; set; }
    public string MailRef { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public string DateText { get; set; } = "";
    public bool IsUnread { get; set; }
    public bool HasAttachments { get; set; }
}

public class EmailDetailDto
{
    public string MailRef { get; set; } = "";
    public string Uid { get; set; } = "";
    public string Subject { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string Cc { get; set; } = "";
    public string DateText { get; set; } = "";
    public bool HasAttachments { get; set; }
    public bool IsUnread { get; set; }
    public string TextBody { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string HtmlPreview { get; set; } = "";
    public List<string> Attachments { get; set; } = new();
}
