# 潮閘 Tidegate

用 Avalonia 做的 macOS 應用程式流量觀測與限速工具。每個應用是一條航道：即時看下載、上傳與合計速度，可以依名稱或速度排序，也可以為單一應用保存下載與上傳上限。

這是獨立作品。概念上參考公開的「依應用程式看速度、設上限」，介面、排列方式和實作都是另做的，不使用 NetLimiter 的程式、圖示或規則格式。

## 執行

需要 .NET 10 SDK。

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="$DOTNET_ROOT:$PATH"
dotnet run --project src/Tidegate.App
```

測試：

```bash
dotnet test Tidegate.slnx
```

## 觀測

每秒讀取 `nettop` 的外部介面累計位元組，用兩次樣本的差計算速度。同一個 `.app` 裡的程序會合併成一個應用程式。迴圈介面不計入。

排序：

- **名稱**：由甲到乙，再點一次反轉。
- **速度**：預設依合計由高到低。可以改看下載或上傳，再點一次反轉。速度相同時依名稱排。

「隱藏低於 1 KB/s」會收起幾乎沒有流量的應用。已經設了上限的應用會留在清單裡。

## 限速

上限以 KB/s 輸入，1 KB = 1000 位元組。留空代表該方向不限制。按「套用上限」會先把欄位裡的數字寫入 `~/Library/Application Support/Tidegate/limits.json`，再要求管理者權限。只按保存則先留在本機，尚未限速。

「套用上限」會要求管理者權限，對這個應用目前的外部連線建立 `dnctl` 管線，並把規則載入 `com.apple/tidegate`。macOS 預設的 pf 規則已經會評估 `com.apple/*` 底下的 dummynet 錨點，所以這裡不修改 `/etc/pf.conf`，也不重載主要規則。套用之後，程式會跟著新連線更新規則，並讓現有連線重新走這條管線。清除系統規則會停下這個更新、清掉錨點與管線，以及這次啟用封包過濾器的參考計數。
