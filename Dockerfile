# 使用 Playwright 官方 .NET 基础镜像（包含 .NET 8 运行时和所有浏览器依赖）
# 需要放在发布后的文件夹中 
# 运行 docker build -t tangyuan-app .    完成打包
FROM mcr.microsoft.com/playwright:v1.58.0-noble

WORKDIR /app

# 复制当前目录所有文件到工作目录
COPY . .

EXPOSE 54123
ENV ASPNETCORE_URLS=http://*:54123
ENV TZ=Asia/Shanghai

# 注意：基础镜像已经配置好沙箱，通常不需要额外参数
# 如果遇到权限问题，可以添加 --no-sandbox，但一般无需
ENTRYPOINT ["dotnet", "TangYuan.dll"]