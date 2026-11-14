# C 盘清理

一个轻量的 Windows 系统盘清理工具。它按规则只读扫描可以清理的文件：临时文件、日志、缓存、安装程序残留、回收站等。每一项都列出大小和来源，只有你勾选并确认后，才会永久删除。

- 55 条清理规则。中风险的规则默认不勾选，并写明删除后会失去什么。
- 大项按来源拆开显示，比如哪个程序留下的安装包、哪个文件夹。不想删的来源取消勾选即可保留。
- 启动后由你点“开始扫描”，扫描只读取，不改动任何文件。
- 正在使用的文件会自动跳过；每次清理都会记录释放了多少空间。

需要 64 位的 Windows 10 或 Windows 11。

## 下载

在 [Releases](https://github.com/ForestSenYT/cdrive-cleaner-kit/releases) 页面下载：

| 文件 | 说明 |
| --- | --- |
| `CDriveCleaner-<版本>-setup.exe` | 安装程序。默认只为当前用户安装，不需要管理员权限，可以在“应用”设置里卸载。 |
| `CDriveCleaner-<版本>-portable.exe` | 便携版，单个文件，直接运行。第一次运行时会把约 8 MB 的运行库解压到 `%TEMP%\.net`。 |

两个版本都自带 .NET 运行时，不需要另外安装。

### 关于签名和 SmartScreen 提示

程序文件用作者的自签名证书签名，证书指纹是：

```
7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D
```

Windows 不认识这张证书，所以第一次运行时 SmartScreen 会提示“Windows 已保护你的电脑”，发布者显示为“未知发布者”。点“更多信息”，再点“仍要运行”即可。以管理员身份扫描时，UAC 提示里的发布者也会显示为未知。

下载后可以用两种方法确认文件没有被改动过：

```powershell
# 1. 和 Release 里的 SHA256SUMS.txt 对照
Get-FileHash .\CDriveCleaner-1.0.0-setup.exe

# 2. 查看签名证书的指纹，应该和上面的一致
(Get-AuthenticodeSignature .\CDriveCleaner-1.0.0-setup.exe).SignerCertificate.Thumbprint
```

不需要、也不建议把这张证书导入“受信任的根证书颁发机构”。

## 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet build
dotnet test
```

图形界面在 `src/CDriveCleaner.App`，命令行版本在 `src/CDriveCleaner.Cli`。清理规则是 [`cdrive-cleaner-kit/rules/rules.json`](cdrive-cleaner-kit/rules/rules.json)，行为定义见 [`SPEC.md`](cdrive-cleaner-kit/SPEC.md)。打包发布的步骤见 [docs/release.md](docs/release.md)。

## 许可

[MIT](LICENSE)
