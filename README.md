# .NET 依赖冲突智能诊断系统

> 右键一键扫描，5 级智能风险评估，让 .NET 版本绑定冲突无处遁形。

## 功能概述

DotNetConflictScanner 是一款 Windows 右键集成工具，能够**深度扫描目标目录下所有 .NET DLL/EXE 的依赖版本冲突**，并结合 **bindingRedirect 规则**、**PublicKeyToken 签名链** 和 **代码路径触达率分析**，给出 5 级智能风险评估报告。

## 5 级智能风险评估

| 级别 | 颜色 | 触发条件 | 运行时后果 |
|------|------|----------|-----------|
| 🚨 **DEADLOCK** | 品红 | 不同版本间 PublicKeyToken 不匹配 | 必崩，redirect 完全无效 |
| ✅ **SAFE** | 绿色 | 已配置 bindingRedirect 重定向 | 安全，无风险 |
| ⚠️ **LOW RISK** | 黄色 | Token=null（弱名称程序集） | 伪冲突，CLR 不做严格版本匹配 |
| 💤 **DORMANT** | 深黄 | 冲突路径不在主 EXE 调用链 | 僵尸代码路径，永不触发 |
| ❌ **CRITICAL** | 大红 | 强名称 + 活跃链 + 无 redirect | 必崩，抛出 `0x80131040` |

## 技术原理

- 使用 `System.Reflection.Metadata.PEReader` + `MetadataReader` 读取程序集元数据（不加载 DLL 到进程，零副作用）
- 自动解析 `*.config` 文件中的 `<dependentAssembly><bindingRedirect>` 规则
- 提取每个引用版本的 **PublicKeyToken** 做跨签名一致性校验
- 解析主程序 .exe 的直接依赖链，反查僵尸代码路径
- 版本号统一归一化为 4 位格式（`1.8.6` → `1.8.6.0`），消除比对偏差

## 系统要求

- Windows 10+ / Windows Server 2016+
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（Desktop Runtime 或 Runtime 均可）

## 快速构建

```bash
# 还原依赖
dotnet restore

# Release 编译
dotnet build -c Release

# 发布为框架依赖单文件 EXE（推荐）
dotnet publish -c Release -r win-x64 --self-contained false

# 产物路径
# bin/Release/net10.0/win-x64/publish/DotNetConflictScanner.exe
```

## 打包安装程序

1. 安装 [Inno Setup 6.x](https://jrsoftware.org/isinfo.php)
2. 用 Inno Setup 打开 `ConflictScannerSetup.iss`
3. 按 `Ctrl+F9` 编译
4. 安装包输出至 `installer_output/DotNetConflictScanner_Setup.exe`

安装后自动注册 Windows 右键菜单：
- **文件夹右键** → "扫描 .NET 版本冲突"（使用选中文件夹路径 `%1`）
- **文件夹空白处右键** → "扫描 .NET 版本冲突"（使用当前文件夹路径 `%V`）

## 命令行使用

```bash
# 指定目录扫描
DotNetConflictScanner.exe "C:\Path\To\Your\App"

# 不传参数则扫描当前目录
DotNetConflictScanner.exe
```

## 项目结构

```
DotNetConflictScanner/
├── Program.cs                  # 核心扫描逻辑（5 级风险评估引擎）
├── DotNetConflictScanner.csproj # .NET 10 项目配置
├── ConflictScannerSetup.iss    # Inno Setup 打包脚本
└── .gitignore
```

## License

MIT
