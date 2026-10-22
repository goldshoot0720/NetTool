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

macOS 沒有單一檔案這種形式，`dotnet publish -r osx-arm64` 產出的是 `.app` bundle。

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

**macOS**：「套用上限」會要求管理者權限，對這個應用目前的外部連線建立 `dnctl` 管線，並把規則載入 `com.apple/tidegate`。macOS 預設的 pf 規則已經會評估 `com.apple/*` 底下的 dummynet 錨點，所以這裡不修改 `/etc/pf.conf`，也不重載主要規則。套用之後，程式會跟著新連線更新規則，並讓現有連線重新走這條管線。清除系統規則會停下這個更新、清掉錨點與管線，以及這次啟用封包過濾器的參考計數。

**Windows**：用 WinDivert 在網路層攔截 TCP 封包，依「本機埠 → 應用程式」對應後排隊節流，下載與上傳都有效。驅動不隨專案附上，要先自己準備：

1. 從 `github.com/basil00/WinDivert/releases` 下載 2.2.x 的 `WinDivert-2.2.2-A.zip`
2. 把 `x64/WinDivert.dll` 與 `x64/WinDivert64.sys` 放到程式目錄（開發時是 `src/Tidegate.App/bin/Debug/net10.0/`）
3. 把該目錄加入 Windows Defender 的排除清單——Defender 會把這個驅動判定成 HackTool（本機實測 ThreatID 451610）

原生 DLL 可以改名成 `Feng33WinDivert.Native.dll`（載入器會先找它，找不到才退回 `WinDivert.dll`），
但驅動必須維持 `WinDivert64.sys`——WinDivert 內部把這個檔名寫死了。改名不影響 Defender 的判定，
它看的是內容與簽章。

行為：

- 只有按「套用上限」才會開啟攔截；按「清除系統規則」或關閉程式就立刻停止，封包恢復原本路徑。
- 節流是「排隊＋漏桶」，不丟包。佇列滿的時候直接放行，寧可暫時不限速，也不讓連線卡住。
- 目前只攔 TCP；UDP 的逐應用程式限速還沒做。
- 沒有驅動時按套用會直接說缺哪個檔案，不會假裝成功。

## 專案結構

| 專案 | 內容 |
|---|---|
| `src/Tidegate.Core` | 與平台無關的邏輯：聚合、排序、速率計算、限速計畫 |
| `src/Tidegate.Platform` | 平台實作：macOS 的 nettop／pf，Windows 的 ESTATS／封包節流 |
| `src/Tidegate.App` | Avalonia 介面 |
| `src/Feng33WinDivert` | WinDivert 2.2 的受控封裝，獨立可重用 |

`Feng33WinDivert` 是照 WinDivert 的 C API 形狀寫的：`WindivertHandle.Open/Receive/Send/Close`、
`WindivertAddress`（80 位元組的封包脈絡）、`WindivertHelper` 的標頭解析。
標頭解析只用受控程式碼，不需要驅動，所以可以單獨測試。

```bash
dotnet pack src/Feng33WinDivert -c Release -o artifacts
```

產出 `Feng33WinDivert.1.0.0.nupkg`。套件不含驅動本體，使用前仍需自備
`WinDivert.dll` 與 `WinDivert64.sys`。授權沿用 LGPL-3.0-or-later。
