# 写入快速跳变的测试配置：ws=向上取整的分钟, ls=+1分钟, le=+2分钟；输出 m0 的 epoch 供调度截图
param([string]$Path)
$m0 = [DateTime]::Now.AddSeconds(95)
$m0 = $m0.AddMilliseconds(-$m0.Millisecond).AddSeconds(-$m0.Second)
$ls = $m0.AddMinutes(1)
$le = $m0.AddMinutes(2)
$json = "{
  `"WorkStart`": `"$($m0.ToString('HH:mm'))`",
  `"LunchStart`": `"$($ls.ToString('HH:mm'))`",
  `"LunchEnd`": `"$($le.ToString('HH:mm'))`",
  `"WorkEnd`": `"23:59`",
  `"OpacityLevel`": 0.85,
  `"EnableBlur`": false
}"
[System.IO.File]::WriteAllText($Path, $json)
Write-Output ([DateTimeOffset] $m0).ToUnixTimeSeconds()
