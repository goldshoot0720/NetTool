# 潮閘 Tidegate

用 Avalonia 做的桌面應用程式流量觀測與限速工具。每個應用是一條航道：即時看下載、上傳與合計速度，可以依名稱或速度排序，也可以為單一應用保存下載與上傳上限。

支援 macOS 與 Windows。兩邊看得到什麼、管得住什麼並不一樣，下面分開說明。

這是獨立作品。概念上參考公開的「依應用程式看速度、設上限」，介面、排列方式和實作都是另做的，不使用 NetLimiter 的程式、圖示或規則格式。

## 執行

需要 .NET 10 SDK。

macOS：

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
dotnet run --project src/Tidegate.App
```

Windows：

```powershell
dotnet run --project src/Tidegate.App
```

Windows 版要讀每個應用程式的流量，得向系統要 TCP 的延伸統計（ESTATS），這只有系統管理員能做。所以啟動時會用 UAC 把自己重新開一份提權的視窗；按「否」的話會退回唯讀畫面，上方會出現提示與「以管理員身分重新啟動」。

讀不到數字時（例如提權失敗、或這台機器不支援 ESTATS），程式會把每次取樣的結果寫到 `%APPDATA%\Tidegate\windows.log`。正常運作時這個檔案只會有啟動與第一次成功取樣兩行。

測試：

```bash
dotnet test Tidegate.slnx
```

## 打包

Windows 單一執行檔（自足，目標機器不需要安裝 .NET）：

```bash
dotnet publish src/Tidegate.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o artifacts/win-x64
```

產出 `artifacts/win-x64/Tidegate.App.exe`，約 50 MB。想小一點可以改成 `--self-contained false`（約 30 MB），但目標機器要先安裝 .NET 10 Desktop Runtime；這種模式不支援 `EnableCompressionInSingleFile`。

單一檔案只包得住程式本身。要限速的話，`WinDivert.dll` 與 `WinDivert64.sys` 仍然得放在 exe 旁邊——驅動必須是磁碟上的實體檔案，核心才載得進來。

macOS 沒有單一檔案這種形式，用打包腳本產出 `.app`：

```bash
./scripts/package-macos.sh
open build/Tidegate.app
```

腳本會自包含發佈、組裝 `Info.plist`、放進圖標並做 ad-hoc 簽章，產物在 `build/Tidegate.app`。直接 `dotnet run` 只會跑出一個裸視窗：沒有 Dock 圖標，選單列顯示「Avalonia Application」，而且子程序（`nettop`、`ps`）在部分環境會被攔下。圖標由 `scripts/make-icon.swift` 產生，產物是 `src/Tidegate.App/Assets/Tidegate.icns`。

沒有開 trimming 或 Native AOT：Avalonia 的 XAML 與繫結走反射，裁剪後容易在執行期才炸掉，省下的體積不值得。

## 觀測

**macOS**：每秒讀取 `nettop` 的外部介面累計位元組，用兩次樣本的差計算速度。同一個 `.app` 裡的程序會合併成一個應用程式。迴圈介面不計入。

**Windows**：每秒讀取系統的 TCP 連線表（含擁有者 PID），對每條連線開啟 ESTATS 取得累計位元組，再把差值累積到程序身上。連線關閉後它已經貢獻的位元組仍留在總量裡，速度不會忽然掉到零。只涵蓋 TCP：Windows 沒有提供 UDP 的逐連線位元組統計，UDP 流量不會出現在清單裡。應用程式名稱取執行檔檔名，同名的執行檔會合併成一條航道。

排序：

- **名稱**：由甲到乙，再點一次反轉。
- **速度**：預設依合計由高到低。可以改看下載或上傳，再點一次反轉。速度相同時依名稱排。

「隱藏低於 1 KB/s」會收起幾乎沒有流量的應用。已經設了上限的應用會留在清單裡。

## 限速

上限以 KB/s 輸入，1 KB = 1000 位元組。留空代表該方向不限制。按「套用上限」會先把欄位裡的數字寫入 `~/Library/Application Support/Tidegate/limits.json`（Windows 是 `%APPDATA%\Tidegate\limits.json`），再要求管理者權限。只按保存則先留在本機，尚未限速。

**macOS**：「套用上限」會要求管理者權限，對這個應用目前的外部連線建立 `dnctl` 管線，並把規則載入 `com.apple/tidegate`。macOS 預設的 pf 規則會評估 `com.apple/*` 底下的 dummynet 錨點，所以這裡不改寫 `/etc/pf.conf`。但只靠 `pfctl -E` 啟用 pf 時，主規則集其實是空的、沒有那條錨點引用，規則寫進去也不會被評估——所以套用時會先確認主規則集有沒有 `anchor "com.apple/*"`，沒有就載入一次 `/etc/pf.conf`（沿用系統預設內容，不修改檔案）。套用之後，程式會跟著新連線更新規則，並讓現有連線重新走這條管線。清除系統規則會停下這個更新、清掉錨點與管線，以及這次啟用封包過濾器的參考計數。

**Windows**：用 WinDivert 在網路層攔截 TCP 封包，依「本機埠 → 應用程式」對應後排隊節流，下載與上傳都有效。驅動不隨專案附上，要先自己準備：

1. 從 `github.com/basil00/WinDivert/releases` 下載 2.2.x 的 `WinDivert-2.2.2-A.zip`
2. 把 `x64/WinDivert.dll` 與 `x64/WinDivert64.sys` 放到程式目錄（開發時是 `src/Tidegate.App/bin/Debug/net10.0/`）
3. 把該目錄加入 Windows Defender 的排除清單——Defender 會把這個驅動判定成 HackTool（本機實測 ThreatID 451610）

原生 DLL 與驅動都必須維持原名：`WinDivert.dll` 與 `WinDivert64.sys`。WinDivert 內部把驅動檔名寫死了，
改名就找不到。改名也不會影響 Defender 的判定，它看的是內容與簽章。

行為：

- 只有按「套用上限」才會開啟攔截；按「清除系統規則」或關閉程式就立刻停止，封包恢復原本路徑。
- 節流是「排隊＋令牌桶」。額度依速率累積，一次取約 5 毫秒份量的封包，等額度補齊再整批送出。
  逐封包 Sleep 的話，1 毫秒的排程誤差就吃掉一成以上的速度（1500 位元組在 1 MB/s 下該隔
  1.5 毫秒，睡成 2 毫秒就只剩 750 KB/s）；批次加上令牌桶的突發容量，多睡掉的時間會變成
  下一批的額度，長期平均才貼得住上限。
- 佇列長度依速率換算成約 250 毫秒的量。佇列滿了就**丟棄**，不放行——放行等於沒有限速；
  丟棄會讓 TCP 重傳並退讓，速度自然壓回上限。
- 額度算的是 TCP 酬載，不含 IP 與 TCP 標頭（約 3%）。標頭是必要開銷，算進去的話介面上
  顯示的速度會比上限低一截。
- 純 ACK 沒有酬載，不佔額度也不排隊，直接放行；排進佇列只會擠掉資料封包，讓整條連線一起卡住。
- 目前只攔 TCP；UDP 的逐應用程式限速還沒做。
- 沒有驅動時按套用會直接說缺哪個檔案，不會假裝成功。

**限制：走本機代理的應用程式管不到。** 如果應用程式把流量交給 `127.0.0.1:port` 的代理
（Clash、V2Ray、公司代理），真正的位元組是代理程序在傳，不是那個應用程式。這時要限速的對象
是代理，不是它。VPN 的 TUN 模式同理。

量測方式（診斷模式，不開介面）：

本機自測——只量節流器本身，不需要驅動、不需要網路、不提權：

```bash
Tidegate.App.exe --selftest [KBps ...]
```

每組速率跑 2 秒，結果印在主控台並寫到 `%APPDATA%\Tidegate\selftest.log`。本機實測：

| 上限 | 實測 | |
|---|---|---|
| 100 KB/s | 101 KB/s | 101.4% |
| 500 KB/s | 503 KB/s | 100.6% |
| 1000 KB/s | 1010 KB/s | 101.0% |
| 5000 KB/s | 5001 KB/s | 100.0% |
| 20000 KB/s | 20004 KB/s | 100.0% |

低速率那一點點超出是起步時桶子是滿的（最多 20 毫秒的突發），不是長期偏差。
同樣的 1 MB/s 情境，舊版漏桶只有 75%。

整條路徑（含驅動）的診斷：

```bash
Tidegate.App.exe --shape <應用程式> <下載KBps> <上傳KBps> [秒數]
```

會自行要求提權，結果寫到 `%APPDATA%\Tidegate\shape-test.log`。每 10 秒一行統計，`sentRate=`
就是節流器實際放行的速度，可以直接拿來對照上限。

兩者的差別：`--selftest` 只看節流器的節奏，`--shape` 會真的攔封包，量到的數字還要看 TCP 的
臉色（丟包、重傳、壅塞窗），而且走本機代理的程式攔不到。要驗證端到端，得用直連的程式跑
`--shape`；走 loopback 也不行，WinDivert 的網路層看不到 `127.0.0.1` 的封包。

## 專案結構

| 專案 | 內容 |
|---|---|
| `src/Tidegate.Core` | 與平台無關的邏輯：聚合、排序、速率計算、限速計畫 |
| `src/Tidegate.Platform` | 平台實作：macOS 的 nettop／pf，Windows 的 ESTATS／封包節流 |
| `src/Tidegate.App` | Avalonia 介面 |

WinDivert 的互操作放在 `src/Tidegate.Platform/Windows/Interop/`，是 `Tidegate.Platform` 的內部實作：
`WindivertHandle`（開／收／送／關）、`WindivertAddress`（80 位元組的封包脈絡）、
`WindivertHelper`（標頭解析）。標頭解析只用受控程式碼，不需要驅動，所以可以單獨測試。

節流的節奏由兩個純受控類別決定：`TokenBucket`（額度怎麼累積、能突發多少）與 `PacedSender`
（兩條佇列、批次、等待、停止時把剩下的送完）。兩者都不碰原生呼叫，所以速率準不準可以在
單元測試裡直接量，不必開驅動。
