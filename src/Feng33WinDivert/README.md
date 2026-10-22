# Feng33WinDivert

WinDivert 2.2 的受控封裝（C# / .NET 10）。提供攔截、讀取、注入封包，以及不依賴驅動的標頭解析。

## 需要什麼

- Windows 7 以上，且程式以系統管理員身分執行
- 官方 WinDivert 2.2 的 `WinDivert.dll` 與 `WinDivert64.sys`，放在程式目錄

原生 DLL 可以改名成 `Feng33WinDivert.Native.dll`（載入器會先找它，找不到才退回 `WinDivert.dll`），
但**驅動必須維持 `WinDivert64.sys`**：WinDivert 內部寫死這個檔名，改名就找不到驅動。

這個套件**不含**驅動本體。WinDivert 由 basil 開發、雙授權 LGPLv3 / GPLv2，請自行從
`github.com/basil00/WinDivert/releases` 取得。驅動常被 Windows Defender 判定成 HackTool，
使用前要把程式目錄加入排除清單——改名不會改變偵測結果，Defender 看的是內容與簽章。

## 用法

```csharp
using Feng33WinDivert;

using var handle = WindivertHandle.Open("tcp and tcp.DstPort == 443");
var buffer = new byte[WindivertHandle.MtuMax];

while (true)
{
    var length = handle.Receive(buffer, out var address);
    if (length == 0)
        break;

    var packet = buffer.AsSpan(0, length);
    if (WindivertHelper.TryGetLocalPort(packet, address.Outbound, out var port))
    {
        // port 就是本機這一端的埠號
    }

    handle.TrySend(packet, address);
}
```

`Receive` 會阻塞到有封包或被關閉；關閉 handle（或 `Dispose`）就會讓它回傳 0。

## 錯誤處理

`WindivertHandle.Open` 失敗時擲 `WindivertException`：

- `ErrorCode`：Windows 錯誤碼，或 `WindivertError.DriverMissing`
- `WindivertError.Describe(code)`：可直接顯示給使用者的說明

## 標頭解析

`WindivertHelper` 只用受控程式碼讀標頭，不需要驅動，因此可以單獨測試：

- `TryGetProtocol`：IPv4/IPv6 的協定編號
- `TryGetTcpPorts` / `TryGetUdpPorts`：來源與目的埠
- `TryGetLocalPort`：依方向取本機埠
- `Matches`：用 WinDivert 過濾語言判斷封包（需要 DLL）

## 授權

本封裝為 LGPL-3.0-or-later。底層 WinDivert 版權屬 basil，雙授權 LGPLv3 / GPLv2。
