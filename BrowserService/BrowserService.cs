using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace AiApi.Services
{
    /// <summary>
    /// 浏览器服务
    /// 负责：
    /// 1. 浏览器单例
    /// 2. Session 管理
    /// 3. 域名白名单检查
    /// </summary>
    public class BrowserService
    {
        private IPlaywright? _playwright;
        private IBrowser? _browser;

        // 浏览器初始化锁（防止并发启动多个浏览器）
        private readonly SemaphoreSlim _browserLock = new(1, 1);

        // Session存储
        private readonly ConcurrentDictionary<string, BrowserSession> _sessions = new();

        // 域名白名单
        private readonly string[] _allowedDomains;
        private readonly bool _enableDomainCheck;

        // Session最大存活时间
        private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(30);

        public BrowserService(IConfiguration configuration)
        {
            _enableDomainCheck = configuration
                .GetValue<bool>("BrowserSecurity:EnableDomainCheck");

            _allowedDomains = configuration
                .GetSection("BrowserSecurity:AllowedDomains")
                .Get<string[]>() ?? Array.Empty<string>();
        }

        /// <summary>
        /// 获取浏览器实例（单例）
        /// </summary>
        private async Task<IBrowser> GetBrowserAsync()
        {
            if (_browser != null)
                return _browser;

            await _browserLock.WaitAsync();

            try
            {
                if (_browser != null)
                    return _browser;

                _playwright = await Playwright.CreateAsync();

                _browser = await _playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        //为了防止阿里云服务器出问题
                        Args = new[]
                        {
                            "--no-sandbox",
                            "--disable-setuid-sandbox",
                            "--disable-dev-shm-usage",
                            "--disable-gpu",
                            "--no-zygote",
                            "--single-process"
                        }
                    });

                return _browser;
            }
            finally
            {
                _browserLock.Release();
            }
        }

        /// <summary>
        /// 创建Session
        /// </summary>
        public async Task<BrowserSession> CreateSessionAsync()
        {
            CleanupExpiredSessions();

            var browser = await GetBrowserAsync();

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize
                {
                    Width = 1400,
                    Height = 900
                }
            });

            var page = await context.NewPageAsync();

            var session = new BrowserSession
            {
                SessionId = Guid.NewGuid().ToString(),
                Context = context,
                CurrentPage = page,
                CreatedTime = DateTime.UtcNow
            };

            _sessions.TryAdd(session.SessionId, session);

            return session;
        }

        /// <summary>
        /// 获取Session
        /// </summary>
        public BrowserSession? GetSession(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;

            _sessions.TryGetValue(id, out var session);

            return session;
        }

        /// <summary>
        /// 获取当前Page
        /// </summary>
        public IPage? GetPage(string sessionId)
        {
            var session = GetSession(sessionId);
            return session?.CurrentPage;
        }

        /// <summary>
        /// 获取所有Session信息
        /// </summary>
        public IEnumerable<object> GetSessions()
        {
            return _sessions.Values.Select(x => new
            {
                x.SessionId,
                x.CreatedTime,
                Url = x.CurrentPage?.Url
            });
        }

        /// <summary>
        /// 关闭Session
        /// </summary>
        public async Task CloseSession(string id)
        {
            if (_sessions.TryRemove(id, out var session))
            {
                try
                {
                    await session.Context.CloseAsync();
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 清理过期Session
        /// </summary>
        private void CleanupExpiredSessions()
        {
            var now = DateTime.UtcNow;

            foreach (var kv in _sessions)
            {
                if (now - kv.Value.CreatedTime > _sessionTimeout)
                {
                    _sessions.TryRemove(kv.Key, out _);

                    try
                    {
                        kv.Value.Context.CloseAsync();
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 域名白名单检查
        /// </summary>
        public bool IsAllowedDomain(string url)
        {
            if (!_enableDomainCheck)
                return true;

            if (string.IsNullOrWhiteSpace(url))
                return false;

            Uri uri;

            try
            {
                uri = new Uri(url);
            }
            catch
            {
                return false;
            }

            var host = uri.Host;

            // "*.*" 表示允许所有网站
            if (_allowedDomains.Any(d => d == "*.*"))
                return true;

            foreach (var domain in _allowedDomains)
            {
                if (host.Contains(domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 浏览器Session
    /// </summary>
    public class BrowserSession
    {
        public string SessionId { get; set; } = "";

        public IBrowserContext Context { get; set; } = default!;

        public IPage CurrentPage { get; set; } = default!;

        public DateTime CreatedTime { get; set; }
    }
}
