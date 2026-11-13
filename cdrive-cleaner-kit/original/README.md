# 原始规则表（参考资料）

`rules.xml` 是被分析的那款清理软件随安装包分发的规则表，这里是解密后的原文：UTF-8 XML，71 条规则。文件里的注释显示它沿用 Dism++ 的规则格式。

它是第三方数据，在这个资源包里的用途是**参考和翻译**：想扩充 `rules/rules.json` 时，从这里找条目，按下面的对照改写成本包的规则格式。产品加载和发布的是 `rules/rules.json`；要把这份原文本身随产品发布，先确认授权。

下面对格式的解释来自对原程序的静态分析，没有做运行验证。

## 结构

```
Data / CleanCollection4 / Item[Name, Level]
    Description, Group, Warning
    Scan  或  ScanCollection / Scan ...
        Applicable   适用条件：FileExist[FilePath]、RegExist[Key, Value]，以及 Or、Not、OSVersion 等组合
        Activate     清理动作：
            General[RootPath, Flags]   根目录
                Query      要匹配的相对路径，可含 *；没有 Query 表示整个根目录
                Excluded   要排除的内容
            Custom[ProcName]           调用 Dism++ 内置的清理过程
```

`Level` 是推荐程度，原程序把 `Level="3"` 的规则设为默认勾选。

## 翻译到 `rules/rules.json`

| 原始规则表 | 本包规则格式 | 说明 |
|---|---|---|
| `Item@Name`、`Group`、`Description` | `name`、`group`、`description` | 另起一个符合格式的 `id` |
| `Warning` | `note` | |
| `Level="3"` | `default_selected: true` | 自行判断是否沿用 |
| `General@RootPath` | `targets[].root` | 一个 `General` 对应一个目标 |
| `Query` | `include` | |
| `Excluded` | `exclude` | 只有写成相对路径的排除项可以直接搬 |
| `FileExist`、`RegExist` | 不需要 | 本包没有条件系统，路径不存在自然没有结果 |

## 不能直接搬的写法

- **`Custom`**：依赖 Dism++ 的内置过程（组件存储清理、驱动清理等），本包不支持。只有 `Custom` 的规则无法翻译。
- **路径宏**：`?GetRegSz(键,值)` 读注册表字符串，`?GetFileVersion(文件)` 取文件版本号，`?GetIniData(文件,节,键)` 按名称看是读配置文件（原程序没有实现它）。本包不支持，带这些宏的规则（主要是第三方软件的"老版本备份"）无法翻译。
- **Dism++ 自定义变量**：下面的对应关系是按上下文推断的，用之前核对。

  | 原文 | 推断含义 | 本包里的写法 |
  |---|---|---|
  | `%System%` | 系统目录 | `%SystemRoot%/System32` |
  | `%SystemX86%` | 32 位系统目录 | `%SystemRoot%/SysWOW64` |
  | `%ProgramFilesX86%` | 32 位程序目录 | `%ProgramFiles(x86)%` |
  | `%Documents%` | 当前用户的文档目录 | 没有对应的环境变量，不支持 |
  | `%0%` | Dism++ 自己的安装目录 | 不适用 |
  | `%Root%` | 当前 `General` 的根目录，只出现在宏的参数里 | 不适用 |

- **以 `*\` 开头的根路径**：原意是所有盘符。本包的 `root` 必须以盘符或变量开头，需要的话改写成 `%SystemDrive%/...`。
- **`Query` 为 `*.*.*.*`**：表示"名字是版本号的目录"，配合排除当前版本来清理旧版本目录。本包不支持。
- **`%ProgramFiles%`**：原程序是 32 位的，这个变量在它那里指向 32 位程序目录。本包要求 64 位进程，同一个写法指向的是 64 位程序目录。

## 原程序对这份规则表的实际处理

翻译时不要假设原文里写的条件都生效过。按静态分析，原程序：

- 只认 `Applicable` 的直接子节点里的 `FileExist` 和 `RegExist`；`Or`、`Not`、`OSVersion` 等连同里面嵌套的条件被整个忽略。
- `ScanCollection` 下有多个 `Scan` 时只取第一个。
- 环境变量只在路径开头展开；用了上面那些 Dism++ 自定义变量的根路径被直接丢弃。
- `Excluded` 只在恰好一条时起作用。
- `Custom` 条目没有对应实现，是空操作。

按这些规则推演，71 条里约 57 条是实际生效的文件规则。
