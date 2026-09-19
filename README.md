# Last.fm Scrobbler — Emby 4.9 / 4.10 适配版

> 本分支（`emby-4.9-4.10`）基于官方停更插件 [MediaBrowser/Last.fm](https://github.com/MediaBrowser/Last.fm)（最后更新 2020-12，目标 Emby 4.2）适配，已在 **Emby 4.9.1.90 与 4.10.0.17 实测可用**：授权、scrobble、Now Playing 均正常。`master` 分支保持与上游官方仓库完全一致。

## 相对上游的修改

| 修改 | 原因 |
|---|---|
| 目标框架升级到 .NET 8，引用 `mediabrowser.server.core 4.9.1.90`，移除 MoreLinq | 适配 Emby 4.9+ 运行时 |
| 授权流程改为 **API Key + Token**（`auth.getToken` → 浏览器授权 → `auth.getSession`） | 官方原用的用户名/密码 `auth.getMobileSession` 已被 Last.fm 对新应用关闭 |
| 配置页**零 JavaScript**：原生 `<form>` POST + 原生链接 + 服务端 302 跳转 | Emby 4.9 注入插件配置页时**不执行内嵌 `<script>`**，原配置页按钮全部失效 |
| 授权端点标记 `[Unauthenticated]`（`MediaBrowser.Controller.Net`） | 原生表单/链接无法携带 `X-Emby-Token` 头，否则被 Emby 401 拦截（报 `Access token is invalid or expired`） |
| 授权链接改用 `https://www.last.fm/api/auth/` | http 会被 301 跳转，token 可能丢失 |
| 授权完成后端点**自锁** | 存在 SessionKey 时拒绝改配置/重新授权，防止公网环境下被匿名篡改 |
| 配置页白底修复 | Emby 4.9+ 插件页以 overlay 渲染，老的 `data-role="page"` 不再提供背景 |

## 安装

1. 从 [Releases](../../releases) 或本仓库构建产物取 `Lastfm.dll`
2. 放入 Emby 的 `plugins/` 目录（直接放根目录，无需子文件夹、无需 deps.json）
3. 重启 Emby → 仪表盘 → 插件 → Last.fm Scrobbler

## 授权步骤

1. 用你的 Last.fm 账号在 <https://www.last.fm/api/account/create> 免费注册 API 应用，拿到 **API Key** 和 **Shared Secret**
2. 插件配置页填入并点「保存 API 配置」
3. 点「① 获取授权链接」→ 自动跳转 Last.fm → 登录并点「允许（Authorize）」
4. 回配置页点「② 完成授权」→ 显示"授权成功，已授权用户：xxx"

之后播放音乐即自动 scrobble。注意：

- token 有效期 **60 分钟且一次性**，①到②之间不要隔太久、不要重复点①
- 授权成功后授权接口自动锁定；需要重新授权时，删除 `plugins/configurations/Lastfm.xml` 中的 `SessionKey` 行并重启 Emby 即可解锁

## 自行构建

```bash
dotnet build -c Release
# 产物：Lastfm/bin/Release/net8.0/Lastfm.dll
```

## 排错

- 配置页按钮点了报 `Access token is invalid or expired`：dll 版本不对（那是 Emby 的 401，本适配版已通过 `[Unauthenticated]` 解决）
- 授权页报 token invalid/expired：token 过期或重复使用，重新从①开始
- 授权失败看 Emby 日志中 `[Lastfm]` 开头的行，会带 Last.fm error code（14=网页上没点允许、4=Secret 错误）

## License

GPL-3.0（与上游一致）
