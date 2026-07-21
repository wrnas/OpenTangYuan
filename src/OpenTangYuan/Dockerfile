# 使用 Playwright 官方 .NET 基础镜像（包含 .NET 8 运行时和所有浏览器依赖）
FROM mcr.microsoft.com/playwright/dotnet:v1.58.0-noble

WORKDIR /app

# 创建 SQLite 数据目录（关键）
RUN mkdir -p /app/data

COPY . .

EXPOSE 54123
ENV ASPNETCORE_URLS=http://*:54123
ENV TZ=Asia/Shanghai

ENTRYPOINT ["dotnet", "TangYuan.dll"]