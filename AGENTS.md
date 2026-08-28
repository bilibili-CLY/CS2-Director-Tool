# CS2-Director-Tool 开发规范

## 分支策略

- **develop** - 默认开发分支，所有新功能和修改应提交到此分支
- **main** - 稳定发布分支，仅在明确要求时合并

## 开发流程

1. 所有开发工作默认在 `develop` 分支进行
2. 当用户明确要求提交到 `main` 分支时：
   - 将 `develop` 合并到 `main`
   - 构建 Windows 可执行文件
   - 发布到 GitHub Releases

## 构建命令

### Windows 构建
```bash
dotnet publish CS2-Director-Tool.App/CS2-Director-Tool.App.csproj -c Release -r win-x64 --self-contained -o ./publish/win-x64
```

### 打包发布
```bash
cd ./publish/win-x64 && zip -r ../../CS2-Director-Tool-win-x64.zip .
```

## 代码规范

- 使用 C# 10+ 语法
- 遵循 MVVM 架构模式
- ViewModel 类以 `ViewModel` 结尾
- View 类以 `Page.axaml` 结尾
- Service 接口以 `I` 前缀命名

## 依赖项

- Avalonia 12.1.1
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Newtonsoft.Json
- obs-websocket-dotnet

## 注意事项

- 修改代码前请先确认在正确的分支
- 发布前确保项目可成功构建
- 提交信息使用简洁描述性语言
