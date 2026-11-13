# 原始规则表翻译记录

把 `cdrive-cleaner-kit/original/rules.xml` 翻译进 `cdrive-cleaner-kit/rules/rules.json` 的对照和取舍记录。翻译方法按 `cdrive-cleaner-kit/original/README.md`。

- 原表有 71 条生效的 `Item`，另有 1 条被原作者注释掉（第 72 行），共 72 条。
- 规则集从 13 条扩充到 55 条：原有 13 条全部保留，id 和原有语义不变；新增 42 条。
- 72 条中 50 条翻译了（8 条只并入已有规则，2 条既并入已有规则又拆出新规则，40 条各成一条新规则），22 条未翻译。

## 逐条对照

“去向”列是规则 id；“已有”表示并入原来的 13 条之一。Level 是原表的推荐程度（3 为原程序默认勾选）。

| # | 原规则 | Level | 去向 | 说明 |
|---|---|---|---|---|
| 1 | 被取代的WinSxS组件 | 2 | 未翻译 | 只有 `Custom`（DismComponentCleanup） |
| 2 | 以前的Windows系统 | 2 | `windows-old` | `Windows.old*` 写成根路径的通配段，只匹配目录 |
| 3 | 以前的Dism++组件 | 3 | 未翻译 | 根路径全是 `%0%`（Dism++ 安装目录），还用了 `?GetLocaleName()` |
| 4 | 360浏览器老版本备份 | 2 | 未翻译 | `*.*.*.*` 版本目录查询，排除项是 `?GetFileVersion` |
| 5 | 阿里旺旺老版本备份 | 2 | 未翻译 | 根路径是 `?GetRegSz`，排除项是 `?GetIniData` |
| 6 | 阿里亲淘老版本备份 | 2 | 未翻译 | 同上 |
| 7 | Chrome浏览器老版本备份 | 2 | 未翻译 | 根路径是 `?GetRegSz`，`*.*.*.*` 加 `?GetFileVersion` |
| 8 | Opera浏览器老版本备份 | 2 | 未翻译 | 同上 |
| 9 | PPLive老版本备份 | 2 | 未翻译 | 根路径是 `?GetRegSz`，`*.*.*.*` 加 `?GetIniData` |
| 10 | 酷狗音乐老版本备份 | 2 | 未翻译 | 根路径和排除项都是 `?GetRegSz` |
| 11 | 2345拼音输入法老版本备份 | 2 | 未翻译 | 同上 |
| 12 | WPS老版本备份 | 2 | 未翻译 | 同上，根路径还带 `..` 段 |
| 13 | 系统自带Appx应用 | 2 | 未翻译 | 只有 `Custom`（DismAppxsCleanup） |
| 14 | Windows报告 | 3 | `error-reports`（已有） | 三个位置与已有规则完全一致 |
| 15 | Windows事件 | 3 | `event-logs` | 第一个 Scan 是 `Custom`，翻译的是第二个 Scan 的 `%System%\winevt\Logs`；改为中风险、不默认勾选 |
| 16 | Windows更新安装记录 | 2 | `update-history` | |
| 17 | Installer目录 | 0 | 未翻译 | 只有 `Custom`（NCInstallerFolderCleanup） |
| 18 | 损坏的Appx | 2 | 未翻译 | 只有 `Custom` |
| 19 | 零售演示离线内容 | 2 | `retail-demo` | |
| 20 | NuGet包缓存 | 2 | `nuget-packages` | 收窄到 `.nuget/packages`，见取舍 3 |
| 21 | Windows下载缓存 | 2 | `update-download`（已有） | 第一个 Scan 与已有规则一致；第二个 Scan 是整个 `SoftwareDistribution`（含更新历史数据库），会扩大范围，未翻译 |
| 22 | 传递优化缓存 | 2 | `delivery-optimization` | 第一个 Scan 是 `Custom`；翻译第二个 Scan，另加 NetworkService 下的传递优化 `Cache`，见取舍 4 |
| 23 | Terminal Server Client缓存 | 3 | `rdp-cache`（已有） | 原表清整个 `Terminal Server Client`，已有规则只清其中的 `Cache`，保持已有的窄范围和“不默认勾选” |
| 24 | XDE缓存文件 | 2 | `xde-cache` | |
| 25 | PDB缓存文件 | 2 | `debugger-symbol-cache` | |
| 26 | Visual Studio智能跟踪 | 1 | `intellitrace-cache` | |
| 27 | Windows预读取文件 | 2 | `prefetch`（已有） | 一致 |
| 28 | 缩略图缓存 | 2 | `thumbnail-cache`（已有） | 新增 `AppData\Local\IconCache.db`；原表的 `Explorer\*` 比已有的 `thumbcache_*.db`、`iconcache_*.db` 宽，没有搬 |
| 29 | WinINet网页缓存 | 3 | `inet-cache`（已有） | 补上 systemprofile（System32、SysWOW64）、`AC\!*\INetCache` 和旧版 Edge 的 `MicrosoftEdge\Cache`；4 个 `Temporary Internet Files` 没搬，见取舍 5 |
| 30 | WinINet Cookies | 2 | `inet-cookies` | 12 个位置全搬；会退出登录，中风险 |
| 31 | Package Cache目录 | 2 | `package-cache` | |
| 32 | Windows Defender保护历史记录 | 2 | `defender-history` | |
| 33 | WebPI缓存 | 2 | `webpi-cache` | |
| 34 | 新版Visual Studio安装源缓存 | 2 | 未翻译 | 只有 `Custom`（NCVSPackageCacheCleanup） |
| 35 | Boot备份信息 | 3 | `startup-backup` | 失去回滚能力，中风险、不默认勾选，见取舍 9 |
| 36 | 硬链接备份 | 2 | `hardlink-backup` | |
| 37 | 会声会影素材存档 | 2 | 未翻译 | 根路径是 `%Documents%`，没有对应的环境变量 |
| 38 | 最后一次正确配置 | 2 | `last-known-good` | |
| 39 | WinSxS临时文件 | 3 | `winsxs-temp` | |
| 40 | Windows日志 | 3 | `windows-logs`（已有）、`windows-dir-backups` | 日志位置全部并入已有规则；`%SystemRoot%\*.bak` 是备份不是日志，单独成中风险规则 |
| 41 | 临时文件 | 3 | `user-temp`、`system-temp`、`service-temp`、`store-app-temp`（已有）、`chkdsk-found` | 补上 `AC\!*\Temp`；`Found.*` 单独成中风险规则；读注册表系统 TEMP 的 `?GetRegSz` 未翻译（默认位置已由 `system-temp` 覆盖）；第二个 Scan 的所有用户 Temp 没搬，见取舍 1 |
| 42 | 常见的驱动临时解压目录 | 3 | `driver-extract-dirs` | AMD、Intel、NVIDIA 三个目录；`Prog` 没搬，见取舍 2 |
| 43 | Windows临时安装文件 | 2 | `windows-setup-temp` | |
| 44 | QQ临时数据 | 2 | `qq-temp` | `%SystemX86%` 写成 `%SystemRoot%/SysWOW64` |
| 45 | YY临时文件 | 2 | `yy-temp` | |
| 46 | 腾讯相关软件下载目录 | 3 | `programdata-temp` | |
| 47 | 微软拼音安装文件 | 3 | `ms-pinyin-setup` | 安装源，中风险、不默认勾选 |
| 48 | 百度网盘日志 | 3 | `baidu-netdisk-logs` | |
| 49 | 飞信日志 | 3 | `fetion-data` | 原表清的是整个 `FetionV5` 目录而不只是日志，按用户数据处理：中风险、不默认勾选 |
| 50 | 回收站 | 3 | `recycle-bin`（已有） | 已有规则用系统接口清空系统盘回收站，保持“不默认勾选” |
| 51 | 系统以及程序崩溃dmp文件 | 1 | `crash-dumps`（已有） | 一致 |
| 52 | 微软系安软无用文件 | 2 | `defender-support` | |
| 53 | Visual Studio日志 | 2 | `vs-settings-logs` | |
| 54 | IDM临时文件 | 2 | 未翻译 | 根路径是 `?GetRegSz`（IDM 的 TempPath），见取舍 15 |
| 55 | CompactOs（较慢） | 0 | 未翻译 | 只有 `Custom`（DismCompactOs） |
| 56 | 硬链接合并（较慢） | 0 | 未翻译 | 只有 `Custom`（DismHardLinkMerge） |
| 57 | 过期驱动 | 0 | 未翻译 | 只有 `Custom`（DismDriverCleanup） |
| 58 | 过期Appx应用 | 0 | 未翻译 | 只有 `Custom`（DismAppxsCleanup） |
| 59 | Installer基线缓存 | 0 | `installer-patch-cache` | 原表“没装苹果软件才提供”的条件无法表达，写进了 `note` |
| 60 | Office安装源 | 0 | `msocache` | `*\` 改写成 `%SystemDrive%` |
| 61 | Office 365/2016安装源 | 0 | `office-c2r-cache` | 两条排除项照搬 |
| 62 | 苹果iTunes备份 | 0 | `itunes-backup` | 用户数据 |
| 63 | 瑞昱声卡驱动安装源缓存 | 0 | `realtek-audio-cache` | `Flags="Directory"` 的 `*` 写成 `*/*`，只取子目录里的文件；两个程序目录都写 |
| 64 | Java安装源缓存 | 0 | `java-install-cache` | |
| 65 | Intel驱动安装源缓存 | 0 | `intel-driver-cache` | |
| 66 | 英伟达驱动安装源缓存 | 0 | `nvidia-installer-cache` | 两个程序目录都写 |
| 67 | 英伟达驱动安装包 | 1 | `nvidia-downloader` | |
| 68 | Microsoft SQL Server更新缓存 | 0 | `sql-server-setup-cache` | 两个 Scan 合并，两个程序目录都写 |
| 69 | Adobe Acrobat安装源缓存 | 0 | `acrobat-setup-files` | 原文是 `%ProgramFilesX86%`，另加了 64 位程序目录，见取舍 4 |
| 70 | 小红伞临时文件 | 0 | `avira-temp` | |
| 71 | Microsoft .NET安装缓存 | 0 | `dotnet-mtp-setup-cache` | 两个程序目录都写 |
| 72 | Appx缓存文件-可解决Appx闪退（原文已注释） | 2 | 未翻译 | 原作者注释掉了，理由是可能损坏正常系统的 Appx |

### 未翻译的原因统计

| 原因 | 条数 | 条目 |
|---|---|---|
| 只有 `Custom` 过程 | 9 | 1、13、17、18、34、55、56、57、58 |
| 注册表或版本宏（`?GetRegSz`、`?GetFileVersion`、`?GetIniData`，多数配合 `*.*.*.*`） | 10 | 4–12、54 |
| `%0%`（Dism++ 安装目录） | 1 | 3 |
| `%Documents%` | 1 | 37 |
| 原文已注释停用 | 1 | 72 |
| 合计 | 22 | |

## 新规则集的分组

| 分组 | 条数 | 规则 |
|---|---|---|
| 临时文件 | 7 | user-temp、system-temp、service-temp、store-app-temp、winsxs-temp、programdata-temp、chkdsk-found |
| 日志与报告 | 7 | error-reports、windows-logs、crash-dumps、event-logs、update-history、defender-history、defender-support |
| 缓存 | 8 | inet-cache、inet-cookies、rdp-cache、thumbnail-cache、prefetch、update-download、delivery-optimization、retail-demo |
| 系统备份与回滚（新） | 6 | windows-old、windows-setup-temp、windows-dir-backups、startup-backup、last-known-good、hardlink-backup |
| 安装程序缓存（新） | 15 | package-cache、installer-patch-cache、msocache、office-c2r-cache、webpi-cache、driver-extract-dirs、intel-driver-cache、nvidia-installer-cache、nvidia-downloader、realtek-audio-cache、java-install-cache、sql-server-setup-cache、acrobat-setup-files、dotnet-mtp-setup-cache、ms-pinyin-setup |
| 应用程序（新） | 11 | nuget-packages、debugger-symbol-cache、intellitrace-cache、xde-cache、vs-settings-logs、qq-temp、yy-temp、baidu-netdisk-logs、fetion-data、avira-temp、itunes-backup |
| 回收站 | 1 | recycle-bin |

36 条中风险，全部不默认勾选，都有 `note`。默认勾选的是 10 条低风险规则：原有的 7 条，加上 winsxs-temp、programdata-temp、baidu-netdisk-logs。

## 取舍和不确定的地方

1. **所有用户的 Temp 没有加。** 原表第 41 条的第二个 Scan 有 `%SystemDrive%\Users\*\AppData\Local\Temp`。用户名较长时 `%TEMP%` 常是 8.3 短路径（如 `C:\Users\ADMINI~1\...`），扫描按路径文本判断归属，当前用户的临时文件会同时被 `user-temp` 和这条规则计入，总量重复。原程序实际也只用第一个 Scan。如果需要清其他账户的临时文件，要先让扫描器按长路径判断归属。
2. **`C:\Prog` 没有搬。** 它不是可辨认的驱动解压目录名，又很容易和用户自建的文件夹重名，整目录清理风险过大。
3. **NuGet 只清 `.nuget/packages`。** 原表清整个 `%USERPROFILE%\.nuget`，其中还可能有 `plugins`（Azure Artifacts 等凭据提供程序），删了会让私有源无法认证。
4. **原表 `Activate` 之外加的两个位置：** 传递优化在 Windows 10 以后真正的缓存位置 `%SystemRoot%/ServiceProfiles/NetworkService/AppData/Local/Microsoft/Windows/DeliveryOptimization/Cache`（原表只在 `Applicable` 里列了它的上级目录，实际清理靠 `Custom`；这里只取 `Cache`，不动状态和日志）；Acrobat 的 64 位程序目录。其余 `%ProgramFiles%` 按约定同时写了 `%ProgramFiles%` 和 `%ProgramFiles(x86)%`。
5. **`Temporary Internet Files` 没搬。** 在 Windows 8 以后它是指向 `INetCache` 的目录联接（本机核实过），内容已由 `INetCache` 目标覆盖，搬过来每个账户只会多一条 `reparse_point_in_root` 警告。
6. **`windows-logs` 扩充的范围。** 加了 `*log.txt`、`SchedLgU.txt`、各用户 AppData 下的系统日志和 CLR 使用日志、应用容器里的 CLR 日志、`%SystemDrive%\Logs`、`System Volume Information\Chkdsk`、`WDI\LogFiles` 和 `System32\LogFiles` 的子目录、`Performance\WinSAT\DataStore`。其中 WinSAT DataStore 是性能评估结果而不是日志（删后系统维护时会重新评估）；`System32\LogFiles` 排除了 `Sum`（Windows Server 的用户访问日志数据库，ESE 格式，不是纯日志）。`*log.txt` 会匹配 Windows 目录下任何以 log.txt 结尾的文件。
7. **原表 Level 3 但不默认勾选的：** `startup-backup`、`driver-extract-dirs`、`ms-pinyin-setup`、`fetion-data`、`chkdsk-found`、`windows-dir-backups`、`event-logs`（都按中风险处理），以及已有的 `rdp-cache`、`recycle-bin`（保持已有设置）。
8. **默认勾选的第三方规则：** `baidu-netdisk-logs`（原表 Level 3，本机核实两个目录里都是日志）。`programdata-temp` 的目录名很通用，不只腾讯在用；`winsxs-temp` 在 Windows 11 上 `ManifestCache` 已经不存在，`CbsTemp` 管理员有完全控制权限。
9. **`pss` 的解释。** 原表说是“修改启动信息后的备份”。按我的了解，系统配置（msconfig）停用启动文件夹里的项目时也会把快捷方式移到这里，所以 `note` 写了两种后果。这一条没有在本机核实（本机没有 `pss`）。
10. **比原表评得更重的：** `debugger-symbol-cache`（不确定 `DBG` 里只有符号）、`xde-cache`（含模拟器快照）、`qq-temp`（含手机助手组件和系统账户下的整个腾讯目录）、`defender-support`（`LocalCopy` 用途不明）、`webpi-cache`（WebPI 已停止服务）、`ms-pinyin-setup`。
11. **比原表 `Warning` 写得更重的 `note`：** `package-cache`（原表说可以正常卸载；但基于 WiX Burn 的程序，如 VC++ 运行库，在“应用和功能”里的卸载入口就是这里缓存的安装程序）；`nvidia-installer-cache`（本机扫描结果里有 `Installer2\InstallerCore`，NVIDIA 组件的卸载入口用它）；`sql-server-setup-cache`（`SQLServer2008R2` 是 2008 R2 的维护程序）。
12. **`Flags="Directory"` 的处理。** 查询写成根路径的通配段（`Windows.old*`、`Found.*`、`jre*.*.*_*`），只匹配目录、跳过重解析点，不会误中同名文件；瑞昱的 `*` 写成 `include: ["*/*"]` 以保留排除项。
13. **`AC\!*` 照字面翻译**（名字以 `!` 开头的子目录）。旧版 Edge 的应用容器子目录印象中是 `#!001` 这类名字，`!*` 匹配不到；Windows 11 上已没有旧版 Edge，没有改写成更宽的 `*!*`。
14. **照抄但可能写错的原路径：** 英伟达的 `%SystemDrive%\users\*\appdata\nvidia\nvbackend\packages`（不在 `Local` 下）；Java 的 `AppData\Roaming\Oracle\Java`（Java 安装缓存通常在 `LocalLow`）。都照原文翻译，没有猜。
15. **IDM 可以近似。** IDM 默认的 TempPath 是 `%APPDATA%\IDM\`，如果接受只认默认位置，可以加一条清理 `%APPDATA%/IDM/DwnlData` 的中风险规则。这里按约定不近似。
16. **变量对照。** `%System%` → `%SystemRoot%/System32`，`%SystemX86%` → `%SystemRoot%/SysWOW64`，`%ProgramFilesX86%` → `%ProgramFiles(x86)%`，`%SystemDrive%\Windows\SysWOW64` → `%SystemRoot%/SysWOW64`，`*\` → `%SystemDrive%`。
17. **分组。** 新增三组：系统备份与回滚、安装程序缓存、应用程序。`update-history` 放在“日志与报告”，Defender 两条也放在那里（系统组件）。

## 需要跟进的地方

- `SPEC.md` 第 0 节把“第三方软件专用规则”列在“不做什么”里，新加的“应用程序”和部分“安装程序缓存”规则和这句冲突，需要改 SPEC 或删规则。
- `tests/CDriveCleaner.Tests/RuleFileLoaderTests.cs` 的 `Ships_rule_file_loads` 断言规则数为 13，现在是 55，这个测试会失败。
- CLI 输出目录里有一份构建时复制的 `rules/rules.json`，重新构建后图形界面和 CLI 默认加载的才是新规则。

## 本机验证（非管理员）

`cdclean scan --rules cdrive-cleaner-kit/rules/rules.json --json` 退出码 0，用时约 0.7–0.9 秒；参考实现 `cleaner_ref.py scan` 退出码 0，用时约 2.3 秒。两者 55 条规则的 `files`、`bytes` 完全一致，两边都没有任何警告。总计 80,422 个文件、约 28.3 GiB（其中当前用户临时文件约 22.5 GiB，回收站约 3.0 GiB）。
