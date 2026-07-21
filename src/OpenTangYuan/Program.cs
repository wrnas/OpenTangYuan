using AiApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using TangYuan.Controllers;
using TangYuan.Models;
using TangYuan.OpenApi;
using WebApi.Tools;
using EverythingSearchClient;

namespace TangYuan
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                var logger = NLog.LogManager.Setup()
                    .LoadConfigurationFromAppSettings()
                    .GetCurrentClassLogger();

                logger.Debug("初始化应用程序");

                var builder = WebApplication.CreateBuilder(args);

                ConfigureServices(builder.Services, builder.Configuration);

                var app = builder.Build();

                ConfigureMiddleware(app, builder.Configuration);

                app.Run();
            }
            catch
            {
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var logger = NLog.LogManager.Setup()
                .LoadConfigurationFromAppSettings()
                .GetCurrentClassLogger();

            // 一键 Demo 模式：
            // 只需要在 appsettings.json 中设置：
            //
            // "DemoMode": true
            //
            // 即可自动启用以下行为：
            // 1. Swagger 页面不弹 Basic Auth 登录框；
            // 2. Swagger UI 不显示 Bearer / API Key Authorize 输入框；
            // 3. Swagger Try it out 调用接口时不因缺少 Token / API Key 返回 401；
            // 4. 不强制 HTTPS 跳转，方便本地 http://localhost:54124 演示。
            //
            // 正式环境设置：
            //
            // "DemoMode": false
            //
            // 即自动恢复正式安全默认值：
            // 1. Swagger 页面需要 Basic Auth；
            // 2. Swagger UI 显示 Bearer 安全定义；
            // 3. 接口使用 JWT / ApiKey 认证；
            // 4. 默认启用 HTTPS 重定向。
            //
            // 如果确实需要高级覆盖，也可以在 appsettings.json 单独设置：
            // "Demo": { "DisableAuthentication": true/false }
            // "Swagger": { "RequireAuth": true/false, "EnableSecurity": true/false }
            // "Https": { "EnableRedirection": true/false }
            var demoMode = configuration.GetValue<bool>("DemoMode");

            var disableAuthenticationForDemo =
                configuration.GetValue<bool?>("Demo:DisableAuthentication") ?? demoMode;

            var enableSwaggerSecurity =
                configuration.GetValue<bool?>("Swagger:EnableSecurity") ?? !demoMode;

            services.AddHttpClient();

            // 浏览器服务（AI 控制浏览器）
            services.AddSingleton<BrowserService>();

            // 注册 DapperHelper
            var connectionString = configuration.GetConnectionString("MsSql");
            services.AddScoped<DapperHelper>(_ => new DapperHelper(connectionString));

            // 添加控制器
            services.AddControllers()
                .AddJsonOptions(options =>
                    options.JsonSerializerOptions.PropertyNamingPolicy = null);

            // Swagger
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "OpenTangYuan API",
                    Version = "v1"
                });

                // DemoMode=true 时，默认不向 Swagger UI 注入 Bearer / API Key 安全定义。
                // 这样打开 Swagger 时不会看到 Authorize 按钮，也不需要输入 Token。
                //
                // DemoMode=false 时，默认启用 Swagger 安全定义，方便正式环境调试受保护接口。
                if (enableSwaggerSecurity)
                {
                    var jwtSecurityScheme = new OpenApiSecurityScheme
                    {
                        Scheme = "Bearer",
                        BearerFormat = "JWT",
                        Name = "Authorization",
                        In = ParameterLocation.Header,
                        Type = SecuritySchemeType.ApiKey,
                        Description = "请输入 'Bearer {token}'"
                    };

                    c.AddSecurityDefinition("Bearer", jwtSecurityScheme);

                    // 如果后续希望 Swagger 同时显示 X-API-Key 输入框，可以开启下面代码。
                    // Demo 模式不建议开启，避免 Release 演示包出现认证干扰。
                    //
                    // var apiKeyScheme = new OpenApiSecurityScheme
                    // {
                    //     Name = "X-API-Key",
                    //     In = ParameterLocation.Header,
                    //     Type = SecuritySchemeType.ApiKey,
                    //     Description = "请输入你的 API Key"
                    // };
                    // c.AddSecurityDefinition("ApiKey", apiKeyScheme);

                    // 根据控制器 / Action 上的 [Authorize] 配置动态添加安全要求。
                    c.OperationFilter<SecurityRequirementsOperationFilter>();

                    c.AddSecurityRequirement(new OpenApiSecurityRequirement
                    {
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "Bearer"
                                }
                            },
                            Array.Empty<string>()
                        }
                    });
                }
            });

            // CORS
            services.AddCors(options =>
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader()));

            // Token 服务
            services.AddSingleton<TokenCacheService>();

            // JWT 配置
            var jwtSecret = configuration["Jwt:Secret"] ?? "fallback-secret-key";
            var issuer = configuration["Jwt:Issuer"] ?? "default-issuer";
            var audience = configuration["Jwt:Audience"] ?? "default-audience";

            logger.Info($"JWT 密钥长度: {jwtSecret.Length} 字符");

            // 认证配置：
            // DemoMode=true 时默认使用 DemoAuthenticationHandler。
            // 它会把所有请求识别为本地 demo 用户，主要用于 Release 演示包和 Swagger smoke test。
            //
            // 重要：
            // 1. 只建议在本地演示环境开启；
            // 2. 不要在生产环境开启；
            // 3. 不要把开启 DemoMode 的 Runtime 暴露到公网。
            if (disableAuthenticationForDemo)
            {
                logger.Warn("Demo authentication bypass is ENABLED. Use only for local demo or offline release packages.");

                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "Demo";
                    options.DefaultChallengeScheme = "Demo";
                })
                    // 处理没有显式指定 scheme 的 [Authorize]。
                    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("Demo", null)
                    // 兼容 [Authorize(AuthenticationSchemes = "ApiKey")]。
                    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>("ApiKey", null)
                    // 兼容 [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]。
                    .AddScheme<AuthenticationSchemeOptions, DemoAuthenticationHandler>(
                        JwtBearerDefaults.AuthenticationScheme,
                        null);
            }
            else
            {
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                    .AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidateAudience = true,
                            ValidateLifetime = true,
                            ValidateIssuerSigningKey = true,
                            ValidIssuer = issuer,
                            ValidAudience = audience,
                            ClockSkew = TimeSpan.Zero
                        };

                        if (jwtSecret.Length >= 64)
                        {
                            logger.Info("使用 RSA 安全密钥");
                            using (var rsa = RSA.Create())
                            {
                                rsa.ImportFromPem(jwtSecret);
                                options.TokenValidationParameters.IssuerSigningKey =
                                    new RsaSecurityKey(rsa.ExportParameters(false));
                            }
                        }
                        else
                        {
                            logger.Info("使用对称安全密钥");
                            options.TokenValidationParameters.IssuerSigningKey =
                                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                        }
                    })
                    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);
            }

            // NLog
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddNLog();
            });

            // 阿里云 OSS
            services.Configure<AliyunOssOptions>(
                configuration.GetSection("AliyunOSS"));

            // 文件系统安全选项
            // 从 appsettings.json 的 "FileSystem" 节读取配置并绑定到 FileSystemOptions。
            services.Configure<FileSystemOptions>(configuration.GetSection("FileSystem"));

            // 限流策略（防止 AI 滥用接口）
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(clientId, _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 100,
                            Window = TimeSpan.FromMinutes(1),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 5
                        });
                });

                options.AddFixedWindowLimiter("AiCallLimiter", opt =>
                {
                    opt.PermitLimit = 10;
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 2;
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsync(
                        "请求过于频繁，请稍后再试。",
                        cancellationToken);
                };
            });
        }

        private static void ConfigureMiddleware(WebApplication app, IConfiguration configuration)
        {
            var demoMode = configuration.GetValue<bool>("DemoMode");

            var requireSwaggerAuth =
                configuration.GetValue<bool?>("Swagger:RequireAuth") ?? !demoMode;

            var enableHttpsRedirection =
                configuration.GetValue<bool?>("Https:EnableRedirection") ?? !demoMode;

            // Swagger 页面访问认证：
            // DemoMode=true：
            //   默认 Swagger:RequireAuth=false，不弹浏览器 Basic Auth 登录框。
            //
            // DemoMode=false：
            //   默认 Swagger:RequireAuth=true，访问 /swagger 需要 Basic Auth。
            //
            // 如果想在正式环境关闭 Swagger 页面 Basic Auth，可以单独设置：
            // "Swagger": { "RequireAuth": false }
            if (requireSwaggerAuth)
            {
                app.Use(async (context, next) =>
                {
                    if (context.Request.Path.StartsWithSegments("/swagger"))
                    {
                        var authHeader = context.Request.Headers.Authorization.ToString();

                        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
                        {
                            context.Response.Headers.WWWAuthenticate = "Basic";
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsync("Unauthorized");
                            return;
                        }

                        try
                        {
                            var swaggerUser = configuration["SwaggerAuth:User"] ?? "admin";
                            var swaggerPassword = configuration["SwaggerAuth:Password"] ?? "password";

                            var encoded = authHeader["Basic ".Length..].Trim();
                            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                            var parts = decoded.Split(':', 2);

                            if (parts.Length != 2 ||
                                parts[0] != swaggerUser ||
                                parts[1] != swaggerPassword)
                            {
                                context.Response.Headers.WWWAuthenticate = "Basic";
                                context.Response.StatusCode = 401;
                                await context.Response.WriteAsync("Unauthorized");
                                return;
                            }
                        }
                        catch
                        {
                            context.Response.Headers.WWWAuthenticate = "Basic";
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsync("Unauthorized");
                            return;
                        }
                    }

                    await next();
                });
            }

            // Swagger UI
            app.UseSwagger();
            app.UseSwaggerUI(c =>
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenTangYuan API V1"));

            // HTTPS 重定向：
            // DemoMode=true 默认关闭，方便本地 http://localhost:54124 演示。
            // DemoMode=false 默认开启，适合正式部署。
            if (enableHttpsRedirection)
            {
                app.UseHttpsRedirection();
            }

            // 静态文件（wwwroot）
            app.UseStaticFiles();

            // 路由
            app.UseRouting();

            // CORS（必须在 UseAuthentication 之前）
            app.UseCors("AllowAll");

            // 限流中间件
            app.UseRateLimiter();

            // 认证与授权
            app.UseAuthentication();
            app.UseAuthorization();

            // 映射控制器
            app.MapControllers();
        }
    }

    /// <summary>
    /// Demo-only authentication handler.
    ///
    /// 这个 Handler 会把所有请求识别为一个本地 Demo 用户。
    /// 它的目的不是提供安全认证，而是让 Release 演示包中的 Swagger 和快速示例
    /// 可以在没有 Token / API Key 的情况下直接运行，避免 401 影响演示体验。
    ///
    /// 只应在以下场景使用：
    /// - 本地 Demo；
    /// - 离线 Release 包；
    /// - 审稿人 smoke test；
    /// - 不连接真实邮箱、真实企业系统、真实内网资源的安全示例环境。
    ///
    /// 不应在以下场景使用：
    /// - 生产环境；
    /// - 内网正式部署；
    /// - 暴露到公网的 Runtime；
    /// - 包含真实邮箱、文件、企业系统权限的环境。
    /// </summary>
    public sealed class DemoAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public DemoAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "demo-user"),
                new Claim(ClaimTypes.Name, "OpenTangYuan Demo User"),
                new Claim(ClaimTypes.Role, "Demo")
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
