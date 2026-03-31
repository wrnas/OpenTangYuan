using AiApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;          // 新增：限流支持
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;               // 新增：限流策略选项
using TangYuan.Controllers;                        // 新增：引用 FileSystemOptions
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
            var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            try
            {
                logger.Debug("初始化应用程序");

                var builder = WebApplication.CreateBuilder(args);

                ConfigureServices(builder.Services, builder.Configuration);

                var app = builder.Build();

                ConfigureMiddleware(app, builder.Configuration);

                app.Run();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "应用程序启动失败");
                throw;
            }
            finally
            {
                NLog.LogManager.Shutdown();
            }
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            services.AddHttpClient();

            // 浏览器服务（AI控制浏览器）
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
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" });

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

                // API Key 安全定义
                var apiKeyScheme = new OpenApiSecurityScheme
                {
                    Name = "X-API-Key",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Description = "请输入你的 API Key"
                };
                c.AddSecurityDefinition("ApiKey", apiKeyScheme);

                // 添加操作过滤器，根据 [Authorize] 中的方案动态添加安全要求
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
            });

            // CORS
            services.AddCors(options =>
                options.AddPolicy("AllowAll", policy =>
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

            // Token服务
            services.AddSingleton<TokenCacheService>();

            // JWT 配置
            var jwtSecret = configuration["Jwt:Secret"] ?? "fallback-secret-key";
            var issuer = configuration["Jwt:Issuer"] ?? "default-issuer";
            var audience = configuration["Jwt:Audience"] ?? "default-audience";

            logger.Info($"JWT 密钥长度: {jwtSecret.Length} 字符");

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
                        logger.Info("使用RSA安全密钥");
                        using (var rsa = RSA.Create())
                        {
                            rsa.ImportFromPem(jwtSecret);
                            options.TokenValidationParameters.IssuerSigningKey = new RsaSecurityKey(rsa.ExportParameters(false));
                        }
                    }
                    else
                    {
                        logger.Info("使用对称安全密钥");
                        options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
                    }
                })
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ApiKey", null);

            // NLog
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddNLog();
            });

            // 阿里云OSS
            services.Configure<AliyunOssOptions>(
                configuration.GetSection("AliyunOSS"));           

            #region 新增：文件系统安全选项配置
            // 从 appsettings.json 中读取 "FileSystem" 节，绑定到 FileSystemOptions
            // 控制器中使用 IOptions<FileSystemOptions> 注入
            services.Configure<FileSystemOptions>(configuration.GetSection("FileSystem"));
            // 如果没有配置则使用默认值（已在 FileSystemOptions 构造函数中指定）
            #endregion

            #region 新增：限流策略（防止 AI 滥用接口）
            // 添加速率限制服务，支持固定窗口、滑动窗口、令牌桶等策略
            services.AddRateLimiter(options =>
            {
                // 全局默认限制策略（可选）
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    // 按用户 IP 或 API Key 进行限流，这里简单按 IP 地址
                    var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,           // 每个窗口允许的最大请求数
                        Window = TimeSpan.FromMinutes(1), // 窗口大小
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 5                // 允许排队等候的请求数
                    });
                });

                // 为特定接口（如 Skills/ExecuteSkill）添加更严格的策略
                options.AddFixedWindowLimiter("AiCallLimiter", opt =>
                {
                    opt.PermitLimit = 10;             // 每分钟最多 10 次调用
                    opt.Window = TimeSpan.FromMinutes(1);
                    opt.QueueLimit = 2;
                });

                // 可选：处理限流触发时的响应
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsync("请求过于频繁，请稍后再试。", cancellationToken);
                };
            });
            #endregion

            #region 注册内部使用业务相关的方法
            // 原有业务注册（如果有）
            #endregion
        }

        private static void ConfigureMiddleware(WebApplication app, IConfiguration configuration)
        {
            // Swagger 基础认证中间件（保持不变）
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

                    var swaggerUser = configuration["SwaggerAuth:User"] ?? "admin";
                    var swaggerPassword = configuration["SwaggerAuth:Password"] ?? "password";

                    try
                    {
                        var encoded = authHeader["Basic ".Length..].Trim();
                        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                        var parts = decoded.Split(':');

                        if (parts.Length < 2 || parts[0] != swaggerUser || parts[1] != swaggerPassword)
                        {
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsync("Unauthorized");
                            return;
                        }
                    }
                    catch
                    {
                        context.Response.StatusCode = 401;
                        await context.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }

                await next();
            });

            // Swagger UI
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1"));

            // HTTPS 重定向
            app.UseHttpsRedirection();

            // 静态文件（wwwroot）
            app.UseStaticFiles();

            // 路由
            app.UseRouting();

            // CORS（必须在 UseAuthentication 之前）
            app.UseCors("AllowAll");

            // 新增：启用速率限制中间件
            // 位置：通常在 UseRouting 之后，UseAuthentication/UseAuthorization 之前或之后均可
            // 建议放在认证之前，以便对未认证的请求也能限流
            app.UseRateLimiter();

            // 认证与授权
            app.UseAuthentication();
            app.UseAuthorization();

            // 映射控制器
            app.MapControllers();

            // 可选：为特定控制器或操作附加限流策略（使用特性方式已在控制器中标记）
            // 例如：app.MapControllers().RequireRateLimiting("AiCallLimiter");  // 全局应用，谨慎使用
        }
    }
}