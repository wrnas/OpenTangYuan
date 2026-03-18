namespace TangYuan
{
    using AiApi.Services;
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.OpenApi.Models;
    using NLog;
    using NLog.Extensions.Logging;
    using NLog.Web;
    using System.Security.Cryptography;
    using System.Text;
    using TangYuan.Models;
    using TangYuan.OpenApi;
    using WebApi.Tools;
    

    public class Program
    {
        public static void Main(string[] args)
        {
            var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
            try
            {                
                logger.Debug("初始化应用程序");

                var builder = WebApplication.CreateBuilder(args);

                // ===== 修复1：确保所有服务注册在ConfigureServices完成 =====
                ConfigureServices(builder.Services, builder.Configuration);

                var app = builder.Build();

                // ===== 修复2：调整中间件顺序 =====
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
                //
                // 新增：API Key 安全定义
                var apiKeyScheme = new OpenApiSecurityScheme
                {
                    Name = "X-API-Key",               // Header 名称，必须与后端认证处理器读取的 Header 一致
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Description = "请输入你的 API Key"
                };
                c.AddSecurityDefinition("ApiKey", apiKeyScheme);

                // 添加操作过滤器，根据 [Authorize] 中的方案动态添加安全要求
                c.OperationFilter<SecurityRequirementsOperationFilter>();
                //
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

            // ===== 修复3：更健壮的JWT配置 =====
            var jwtSecret = configuration["Jwt:Secret"] ?? "fallback-secret-key";
            var issuer = configuration["Jwt:Issuer"] ?? "default-issuer";
            var audience = configuration["Jwt:Audience"] ?? "default-audience";

            // 添加此检查以确认密钥类型
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

                    // 根据密钥长度自动选择加密类型
                    if (jwtSecret.Length >= 64)
                    {
                        logger.Info("使用RSA安全密钥");
                        using (var rsa = RSA.Create())  // 自动选择适合的 RSA 实现
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

            ;

            // NLog
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddNLog();
            });

            // 阿里云OSS
            services.Configure<AliyunOssOptions>(
                configuration.GetSection("AliyunOSS"));

            #region 注册内部使用业务相关的方法
           

            #endregion


        }

        private static void ConfigureMiddleware(WebApplication app, IConfiguration configuration)
        {
            // ===== 修复4：改为异步中间件 =====
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

            // 中间件顺序很重要
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1"));

            // HTTPS
            app.UseHttpsRedirection();

            //允许访问 wwwroot 文件
            app.UseStaticFiles();

            // 路由
            app.UseRouting();            

            // ===== 修复5：确保CORS在认证之前 =====
            app.UseCors("AllowAll");

            // 认证中间件
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
        }
    }
}