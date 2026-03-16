using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TangYuan.OpenApi
{
    public class SecurityRequirementsOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // 获取控制器和方法上的 Authorize 特性
            var controllerAuth = context.MethodInfo.DeclaringType?.GetCustomAttributes(true)
                .OfType<AuthorizeAttribute>().Select(attr => attr.AuthenticationSchemes).FirstOrDefault();
            var methodAuth = context.MethodInfo.GetCustomAttributes(true)
                .OfType<AuthorizeAttribute>().Select(attr => attr.AuthenticationSchemes).FirstOrDefault();

            var authSchemes = methodAuth ?? controllerAuth;
            if (string.IsNullOrEmpty(authSchemes))
                return; // 没有 [Authorize] 特性，无需安全要求

            // 按逗号分割方案名（可能同时指定了多个方案，如 "Bearer,ApiKey"）
            var schemes = authSchemes.Split(',').Select(s => s.Trim()).ToList();

            // 为每个方案添加安全要求
            operation.Security = new List<OpenApiSecurityRequirement>();
            foreach (var scheme in schemes)
            {
                var securityScheme = new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = scheme   // 必须与 AddSecurityDefinition 中的 ID 一致
                    }
                };
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [securityScheme] = new List<string>()
                });
            }
        }
    }
}