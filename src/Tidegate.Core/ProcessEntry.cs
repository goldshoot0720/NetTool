namespace Tidegate.Core;

/// <summary>
/// ps 看到的一個行程：父行程與可執行檔路徑。
/// 路徑用來還原 .app 名稱，父行程用來把輔助程式歸給它所屬的 App。
/// </summary>
public readonly record struct ProcessEntry(int ParentProcessId, string Command);
