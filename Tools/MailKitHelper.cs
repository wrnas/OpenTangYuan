using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

/// <summary>
/// MailKit 邮件通用操作封装（静态类，从 appsettings.json 读取配置）
/// 适配 .NET 8 + MailKit/MimeKit 4.16.0
/// </summary>
public static class MailKitHelper
{
    #region 配置加载

    private static IConfigurationRoot? _configuration;
    private static readonly object _lock = new();

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

    #region 基础辅助方法

    private static string GetString(Dictionary<string, object>? args, string key, string defaultValue = "")
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

    private static List<string> GetStringList(Dictionary<string, object>? args, string key)
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
                    var text = item.ValueKind == JsonValueKind.String
                        ? item.GetString()?.Trim()
                        : item.ToString()?.Trim();

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

        if (value is string s)
        {
            if (!string.IsNullOrWhiteSpace(s))
                result.Add(s.Trim());
            return result;
        }

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var item in enumerable)
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

    private static bool GetBool(Dictionary<string, object>? args, string key, bool defaultValue = false)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return defaultValue;

        if (value is bool b)
            return b;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.String && bool.TryParse(je.GetString(), out var parsedFromJe))
                return parsedFromJe;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }

    private static List<string> SplitEmails(string input)
    {
        return (input ?? "")
            .Split(new[] { ',', ';', '，', '；', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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

        string? dir = Path.GetDirectoryName(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath);
        int counter = 1;
        string newPath;

        do
        {
            newPath = Path.Combine(dir ?? "", $"{name}_{counter}{ext}");
            counter++;
        } while (File.Exists(newPath));

        return newPath;
    }

    private static string StripHtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";

        string text = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</p>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<.*?>", "");
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
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

    #endregion

    #region 中文乱码修复

    private static string FixPossibleMojibake(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? "";

        string fixedText = TryDecodeLatin1ToGb18030(text);

        if (LooksMoreReadableChinese(text, fixedText))
            return fixedText;

        return text;
    }

    private static string TryDecodeLatin1ToGb18030(string text)
    {
        try
        {
            byte[] bytes = Encoding.Latin1.GetBytes(text);
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
        catch
        {
            return text;
        }
    }

    private static bool LooksMoreReadableChinese(string original, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        int originalChinese = CountChineseChars(original);
        int candidateChinese = CountChineseChars(candidate);

        if (candidateChinese > originalChinese)
            return true;

        bool originalLooksMojibake =
            original.Contains('²') ||
            original.Contains('Ê') ||
            original.Contains('Ï') ||
            original.Contains('Í') ||
            original.Contains('µ') ||
            original.Contains('·') ||
            original.Contains('¨') ||
            original.Contains('Ö');

        return originalLooksMojibake && candidateChinese > 0;
    }

    private static int CountChineseChars(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                count++;
        }
        return count;
    }

    #endregion

    #region SMTP / IMAP 连接

    private static async Task<ImapClient> ConnectImapAsync(CancellationToken cancellationToken = default)
    {
        try
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

            // 网易系邮箱常见要求：在 EXAMINE/SELECT 前发送 IMAP ID
            try
            {
                var impl = new ImapImplementation
                {
                    Name = "TangYuan",
                    Version = "1.0.0",
                    Vendor = "TangYuan"
                };

                await client.IdentifyAsync(impl, cancellationToken);
            }
            catch
            {
                // 某些服务器不支持，可忽略
            }

            return client;
        }
        catch (Exception ex) when (ex.Message.Contains("Unsafe Login", StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception("IMAP 登录被邮箱服务器拦截：请确认已开启 IMAP，并使用客户端授权码。对于 163/126/188 等邮箱，通常还需要在认证后发送 IMAP ID。原始错误：" + ex.Message);
        }
    }

    #endregion

    #region 发送邮件

    /// <summary>
    /// 发送邮件。
    /// 支持：
    /// 1. 普通正文 body
    /// 2. HTML 正文 isBodyHtml=true
    /// 3. 普通附件 attachments / attachment
    /// 4. AI 友好的正文插图 insertImagePaths
    /// 5. 兼容旧版 embeddedImages + htmlBody
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
                string cid = kv.Key;
                string imagePath = kv.Value;

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
                    sb.Append(System.Net.WebUtility.HtmlEncode(body)
                        .Replace("\r\n", "<br/>")
                        .Replace("\n", "<br/>"));
                }

                int idx = 1;
                foreach (var imgPath in insertImagePaths)
                {
                    var image = builder.LinkedResources.Add(imgPath);
                    image.ContentId = $"img{idx}";

                    if (sb.Length > 0)
                        sb.Append("<br/>");

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
        await client.ConnectAsync(
            smtpServer,
            smtpPort,
            smtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);

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

    #region 邮件引用标识

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

    #endregion

    #region 搜索邮件（AI友好）

    /// <summary>
    /// 搜索邮件摘要（AI友好 DTO）
    /// 注意：
    /// - 不依赖服务器端中文 SEARCH，避免 163 等服务器对中文主题搜索不稳定
    /// - 先取最近一批邮件，再在本地做中文主题/正文匹配
    /// - 对可能出现的 GBK/GB18030 乱码主题做修复
    /// </summary>
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

        SearchQuery query = SearchQuery.All;

        if (!string.IsNullOrWhiteSpace(fromKeyword))
            query = query.And(SearchQuery.FromContains(fromKeyword));

        var uids = await inbox.SearchAsync(query, cancellationToken);

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

            string rawSubject = message.Subject ?? summary.Envelope?.Subject ?? "";
            string subject = FixPossibleMojibake(rawSubject);

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

    #endregion

    #region 读取完整邮件

    public static async Task<MimeMessage> GetMessageByUidAsync(UniqueId uid, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        return await inbox.GetMessageAsync(uid, cancellationToken);
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

        string fixedSubject = FixPossibleMojibake(message.Subject);
        string textBody = message.TextBody ?? "";
        string htmlBody = message.HtmlBody ?? "";
        string htmlPreview = StripHtmlToText(htmlBody);

        return new EmailDetailDto
        {
            MailRef = BuildMailRef(uid),
            Uid = uid.Id.ToString(),
            Subject = string.IsNullOrWhiteSpace(fixedSubject) ? "(无主题)" : fixedSubject,
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

    #endregion

    #region 下载附件

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

    #region 标记已读

    public static async Task MarkAsReadAsync(UniqueId uid, CancellationToken cancellationToken = default)
    {
        using var client = await ConnectImapAsync(cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);
        await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);
    }

    #endregion

    #region 回复邮件

    public static async Task ReplyToEmailAsync(
        UniqueId originalUid,
        string replyText,
        string? replyHtml = null,
        bool replyToAll = false,
        List<string>? attachments = null,
        CancellationToken cancellationToken = default)
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

        if (original.ReplyTo.Count > 0)
            reply.To.AddRange(original.ReplyTo);
        else if (original.From.Count > 0)
            reply.To.AddRange(original.From);
        else if (original.Sender != null)
            reply.To.Add(original.Sender);

        if (replyToAll)
        {
            foreach (var m in original.To.Mailboxes.Where(m => !string.Equals(m.Address, senderEmail, StringComparison.OrdinalIgnoreCase)))
                reply.To.Add(m);

            foreach (var m in original.Cc.Mailboxes.Where(m => !string.Equals(m.Address, senderEmail, StringComparison.OrdinalIgnoreCase)))
                reply.Cc.Add(m);
        }

        reply.Subject = original.Subject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
            ? original.Subject
            : "Re: " + (original.Subject ?? "");

        if (!string.IsNullOrEmpty(original.MessageId))
        {
            reply.InReplyTo = original.MessageId;

            foreach (var id in original.References)
                reply.References.Add(id);

            reply.References.Add(original.MessageId);
        }

        var builder = new BodyBuilder();
        string quotedPlain = BuildQuotedText(original);
        string quotedHtml = BuildQuotedHtml(original);

        if (!string.IsNullOrWhiteSpace(replyHtml))
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
            {
                builder.Attachments.Add(file);
            }
        }

        reply.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        await smtp.ConnectAsync(
            smtpServer,
            smtpPort,
            smtpUseSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
            cancellationToken);

        await smtp.AuthenticateAsync(senderEmail, senderPassword, cancellationToken);
        await smtp.SendAsync(reply, cancellationToken);
        await smtp.DisconnectAsync(true, cancellationToken);
    }

    #endregion

    #region 保存 eml

    public static async Task SaveToEmlAsync(UniqueId uid, string filePath, CancellationToken cancellationToken = default)
    {
        var message = await GetMessageByUidAsync(uid, cancellationToken);
        using var stream = File.Create(filePath);
        await message.WriteToAsync(stream, cancellationToken);
    }

    #endregion

    #region 引用原文辅助

    private static string BuildQuotedText(MimeMessage original)
    {
        string plain = original.TextBody ?? "（无文本内容）";
        return $@"
-------- 原始邮件 --------
主题：{FixPossibleMojibake(original.Subject)}
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
<p>主题：{FixPossibleMojibake(original.Subject)}<br>
发件人：{original.From}<br>
时间：{original.Date:yyyy-MM-dd HH:mm:ss}</p>
{html}";
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
