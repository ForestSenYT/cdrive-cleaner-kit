# 发布新版本

## 准备（每台电脑做一次）

- .NET 10 SDK
- Inno Setup 6：`winget install JRSoftware.InnoSetup`
- 签名证书：放在“当前用户\个人”证书存储里、带私钥的代码签名证书。1.0.0 用的是作者的自签名证书，指纹 `7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D`，有效期到 2031 年 11 月。

换电脑或重装系统后，这张证书会跟着丢失，所以要备份私钥。导出时会提示输入密码，导出的 `.pfx` 不要提交到仓库（`.gitignore` 已经排除了 `*.pfx`）：

```powershell
$password = Read-Host -AsSecureString '给 pfx 设一个密码'
Export-PfxCertificate -Cert Cert:\CurrentUser\My\7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D -FilePath D:\backup\cdclean-signing.pfx -Password $password

# 在新电脑上导入
Import-PfxCertificate -FilePath D:\backup\cdclean-signing.pfx -CertStoreLocation Cert:\CurrentUser\My -Password $password
```

如果证书丢了，可以新建一张。新证书的指纹会变，README 里写的指纹也要跟着改：

```powershell
New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=ForestSenYT' -CertStoreLocation Cert:\CurrentUser\My `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(5)
```

## 每次发布

1. 修改 `Directory.Build.props` 里的 `Version`，合并到 `main`。
2. 在 `main` 上生成发布文件。这一步会先运行测试，任何一步失败都会停下来：

   ```powershell
   .\build\release.ps1 -Thumbprint 7EB8C6545406E0F7AEC1E35CB27A18EFC9CA937D
   ```

   结果放在 `artifacts\release`：安装程序、便携版、公钥证书 `CDriveCleaner-signing.cer` 和 `SHA256SUMS.txt`。

3. 在本机试装一次：运行安装程序，打开程序扫描一次，再从“设置 → 应用”里卸载。
4. 打标签，建草稿 release，在 GitHub 上检查后再点发布：

   ```powershell
   git tag -a v1.0.0 -m "C 盘清理 1.0.0"
   git push origin v1.0.0
   gh release create v1.0.0 --draft --title "C 盘清理 1.0.0" --notes-file <发布说明.md> (Get-ChildItem artifacts\release\*).FullName
   ```

## 签名说明

`build\sign.ps1` 签名的对象：我们自己的 exe 和 dll、便携版 exe、安装程序，以及安装程序写出的卸载程序。.NET 运行时的文件本来就带着微软的签名。每个签名都通过 DigiCert 的时间戳服务器加了时间戳，证书过期后签名仍然有效；时间戳请求只发送签名的哈希值。

自签名证书不被 Windows 信任，所以用户会看到 SmartScreen 提示和“未知发布者”。想去掉这些提示，需要换成受信任的证书，比如开源项目可以申请的 SignPath Foundation 免费签名，或者 Certum 的开源开发者证书。换证书时只需要改 `-Thumbprint` 参数。
