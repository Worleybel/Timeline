# 一键发布 Timline：打包自包含单文件 exe → 创建 GitHub Release → 上传附件
# 用法：
#   powershell -File tools\release.ps1                  # 版本号自动在最新 tag 上递增 patch
#   powershell -File tools\release.ps1 -Version 2.0.0   # 指定版本号
#   powershell -File tools\release.ps1 -DryRun          # 只看计划，不打包不上传
# 认证复用 git 凭据管理器（先 git push 过一次即可），无需手动填 token。
param(
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$DryRun,
    [switch]$Open
)

$ErrorActionPreference = 'Stop'

# ---- 编码统一为 UTF-8：PS5.1 默认按本地代码页解码 git 输出、按 ISO-8859-1 发送
# 请求体，中文提交信息会变问号（无控制台宿主下该赋值可能失败，不致命） ----
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false } catch { }

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project  = Join-Path $RepoRoot 'Timline'
$PublishDir = Join-Path $RepoRoot 'publish'
$Repo = 'Worleybel/Timeline'

# ---- GitHub 认证：从 git 凭据管理器取 token，避免硬编码 ----
# PS5.1 直接管道多行字符串给 git credential 会丢协议字段，须借临时文件重定向
$qfile = Join-Path $env:TEMP 'timline-credquery.txt'
[IO.File]::WriteAllText($qfile, "protocol=https`nhost=github.com`n")
$cred = cmd /c "`"git`" credential fill < `"$qfile`""
Remove-Item $qfile -ErrorAction SilentlyContinue
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=', ''
if (-not $token) { throw '未找到 GitHub 凭据，请先手动 git push 一次以保存凭据' }
$headers = @{ Authorization = "token $token"; Accept = 'application/vnd.github+json' }

function Invoke-GhApi {
    [CmdletBinding()]
    param($Method, $Uri, $Body)
    $params = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    # body 必须显式转 UTF-8 字节：PS5.1 对字符串 body 默认 ISO-8859-1，中文会丢成 ?
    if ($Body) {
        $params.Body = [Text.Encoding]::UTF8.GetBytes($Body)
        $params.ContentType = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

# ---- 同步远端 tag（失败不致命：离线时退回本地 tag）----
$prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
git fetch --tags --force --quiet 2>$null | Out-Null
$ErrorActionPreference = $prevEap

# ---- 版本号：取参数，否则在最新 tag 基础上 patch+1 ----
$latest = git tag --list 'v*' |
    ForEach-Object { [version]$_.TrimStart('v') } |
    Sort-Object | Select-Object -Last 1
if ($Version) {
    $newVer = [version]$Version
} elseif ($latest) {
    $newVer = [version]::new($latest.Major, $latest.Minor, [Math]::Max($latest.Build, 0) + 1)
} else {
    $newVer = [version]'1.0.0'
}
$tag = "v$newVer"
$exeName = "Timline-$tag-win-x64.exe"
Write-Host "==> 目标版本：$tag（当前最新：$(if ($latest) { "v$latest" } else { '无' })）"

# ---- 源码状态检查：有未提交修改时提醒，落后远端时自动 push ----
$dirty = @(git status --porcelain)
if ($dirty.Count -gt 0) {
    Write-Warning '工作区有未提交的修改，发布产物将包含这些改动：'
    $dirty | ForEach-Object { Write-Host "    $_" }
}
$ahead = @(git rev-list "origin/main..HEAD" 2>$null)
if ($ahead.Count -gt 0 -and -not $DryRun) {
    Write-Host "==> 本地领先远端 $($ahead.Count) 个提交，先 push..."
    git push
    if ($LASTEXITCODE -ne 0) { throw 'git push 失败，请检查网络或凭据' }
}

# ---- 防重复发布：tag 已存在则中止（404 = 尚未发布，属预期）----
$existing = Invoke-GhApi GET "https://api.github.com/repos/$Repo/releases/tags/$tag" -ErrorAction SilentlyContinue
if ($existing) { throw "Release $tag 已存在（$($existing.html_url)），请换一个版本号" }

if ($DryRun) {
    Write-Host '==> DryRun 计划：'
    Write-Host "    dotnet publish -c Release -r win-x64（自包含单文件 + 压缩，版本号 $newVer）"
    Write-Host "    创建 Release $tag 并上传附件 $exeName"
    if ($latest) { Write-Host "    变更日志范围：v$latest..HEAD" }
    return
}

# ---- 打包 ----
if (-not $SkipBuild) {
    Write-Host '==> dotnet publish（自包含单文件 + 压缩）...'
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    dotnet publish $Project -c Release -r win-x64 `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$newVer `
        --self-contained -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }
    Rename-Item (Join-Path $PublishDir 'Timline.exe') $exeName
}
$exe = Join-Path $PublishDir $exeName
if (-not (Test-Path $exe)) { throw "找不到产物 $exe" }
$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "    产物：$exeName（$sizeMb MB）"

# ---- 发布说明：自动汇总上一个 tag 以来的提交 ----
$leaf = Split-Path $exe -Leaf
$notes = New-Object System.Collections.Generic.List[string]
$notes.Add("Timline $tag")
$notes.Add('')
if ($latest) {
    $commits = @(git log "v$latest..HEAD" --oneline --no-decorate)
    if ($commits.Count -gt 0) {
        $notes.Add('## 变更')
        $notes.Add('')
        $commits | ForEach-Object { $notes.Add("- $_") }
        $notes.Add('')
    }
}
$notes.Add('## 下载')
$notes.Add('')
$notes.Add("- **$leaf** — 自包含单文件（$sizeMb MB），内置 .NET 10 运行时，下载后直接双击运行（Windows 10 1809+ / Windows 11 x64）。")
$body = $notes -join "`n"
$payload = @{
    tag_name = $tag; target_commitish = 'main'
    name = "Timline $tag"; body = $body
    draft = $false; prerelease = $false
} | ConvertTo-Json

# ---- 创建 Release + 上传附件 ----
Write-Host "==> 创建 Release $tag..."
$release = Invoke-GhApi POST "https://api.github.com/repos/$Repo/releases" $payload
Write-Host "    $($release.html_url)"

# PS5.1 的 Invoke-RestMethod 上传大文件会断连，改用 Windows 10+ 自带的 curl.exe
Write-Host "==> 上传附件 $leaf（$sizeMb MB，视网速需要几分钟）..."
$curlFile = "@$($exe -replace '\\', '/')"
$respFile = Join-Path $env:TEMP 'timline-upload.json'
& curl.exe -# -X POST "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$leaf" `
    -H "Authorization: token $token" -H 'Content-Type: application/octet-stream' `
    --data-binary $curlFile -o $respFile
if ($LASTEXITCODE -ne 0) { throw "curl 上传失败（exit $LASTEXITCODE）" }
$asset = Get-Content $respFile -Raw | ConvertFrom-Json
Remove-Item $respFile -ErrorAction SilentlyContinue
Write-Host "    $($asset.browser_download_url)"

# ---- 收尾：把远端新 tag 同步回本地，供下次递增 ----
$prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
git fetch --tags --force --quiet 2>$null | Out-Null
$ErrorActionPreference = $prevEap

Write-Host "==> 发布完成 🎉 $tag"
if ($Open) { Start-Process $release.html_url }
