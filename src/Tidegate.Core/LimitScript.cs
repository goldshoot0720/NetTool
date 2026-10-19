using System.Text;

namespace Tidegate.Core;

public static class LimitScript
{
    public static long Kilobits(long bitsPerSecond) =>
        Math.Max(1, (bitsPerSecond + 999) / 1000);

    public static string FormatPipeLine(PipeSpec pipe)
    {
        ValidatePipes([pipe]);
        var kilobits = Kilobits(pipe.BitsPerSecond);
        if (kilobits > 80_000_000)
            throw new ArgumentOutOfRangeException(nameof(pipe), "管線頻寬超出可設定範圍。");
        return $"{pipe.Number} {kilobits}";
    }

    public static string BuildApply(string directory, string tokenPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/bash");
        builder.AppendLine("set -u");
        builder.AppendLine($"DIR={BashQuote(directory)}");
        builder.AppendLine($"TOKEN_FILE={BashQuote(tokenPath)}");
        builder.AppendLine("RULES=\"$DIR/rules.pf\"");
        builder.AppendLine("PIPES=\"$DIR/pipes.txt\"");
        builder.AppendLine("ON=\"$DIR/helper.on\"");
        builder.AppendLine("PIDFILE=\"$DIR/helper.pid\"");
        builder.AppendLine("STATUS=\"$DIR/live.status\"");
        builder.AppendLine("HELPER=\"$DIR/helper.sh\"");
        builder.AppendLine("if [[ ! -f \"$TOKEN_FILE\" ]]; then");
        builder.AppendLine("  token=\"\"");
        builder.AppendLine("  while IFS= read -r line; do");
        builder.AppendLine("    case \"$line\" in");
        builder.AppendLine("      \"Token : \"*) token=\"${line#Token : }\" ;;");
        builder.AppendLine("    esac");
        builder.AppendLine("  done < <(/sbin/pfctl -E 2>&1)");
        builder.AppendLine("  if [[ -z \"$token\" ]]; then");
        builder.AppendLine("    echo \"無法取得封包過濾器權杖\" >&2");
        builder.AppendLine("    exit 3");
        builder.AppendLine("  fi");
        builder.AppendLine("  printf '%s\\n' \"$token\" > \"$TOKEN_FILE\"");
        builder.AppendLine("fi");
        builder.AppendLine("if [[ -f \"$PIDFILE\" ]]; then");
        builder.AppendLine("  old=$(/bin/cat \"$PIDFILE\" 2>/dev/null || true)");
        builder.AppendLine("  if [[ -n \"$old\" ]]; then");
        builder.AppendLine("    /bin/kill \"$old\" >/dev/null 2>&1 || true");
        builder.AppendLine("  fi");
        builder.AppendLine("fi");
        builder.AppendLine("/bin/rm -f \"$ON\" \"$STATUS\"");
        builder.AppendLine("cat > \"$HELPER\" << 'END'");
        builder.AppendLine(HelperBody);
        builder.AppendLine("END");
        builder.AppendLine("/bin/chmod 700 \"$HELPER\"");
        builder.AppendLine("/usr/bin/touch \"$ON\"");
        builder.AppendLine("/usr/bin/perl -MPOSIX -e 'fork() and exit; setsid(); fork() and exit; exec @ARGV or die $!' /bin/bash \"$HELPER\" \"$DIR\"");
        builder.AppendLine("for _ in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15; do");
        builder.AppendLine("  if [[ -s \"$STATUS\" ]]; then");
        builder.AppendLine("    break");
        builder.AppendLine("  fi");
        builder.AppendLine("  /bin/sleep 0.2");
        builder.AppendLine("done");
        builder.AppendLine("if [[ ! -s \"$STATUS\" ]]; then");
        builder.AppendLine("  echo \"管線沒有在時限內套用\" >&2");
        builder.AppendLine("  exit 6");
        builder.AppendLine("fi");
        builder.AppendLine("/bin/cat \"$STATUS\"");
        builder.AppendLine("if ! /usr/bin/grep -q '^已套用' \"$STATUS\"; then");
        builder.AppendLine("  exit 7");
        builder.AppendLine("fi");
        return builder.ToString();
    }

    public static string BuildClear(string tokenPath)
    {
        var directory = Path.GetDirectoryName(tokenPath);
        if (string.IsNullOrEmpty(directory))
            throw new ArgumentException("權杖路徑必須位於目錄中。", nameof(tokenPath));

        var builder = new StringBuilder();
        builder.AppendLine("#!/bin/bash");
        builder.AppendLine("set -u");
        builder.AppendLine($"DIR={BashQuote(directory)}");
        builder.AppendLine($"TOKEN_FILE={BashQuote(tokenPath)}");
        builder.AppendLine("/bin/rm -f \"$DIR/helper.on\"");
        builder.AppendLine("if [[ -f \"$DIR/helper.pid\" ]]; then");
        builder.AppendLine("  old=$(/bin/cat \"$DIR/helper.pid\" 2>/dev/null || true)");
        builder.AppendLine("  if [[ -n \"$old\" ]]; then");
        builder.AppendLine("    /bin/kill \"$old\" >/dev/null 2>&1 || true");
        builder.AppendLine("  fi");
        builder.AppendLine("  /bin/rm -f \"$DIR/helper.pid\"");
        builder.AppendLine("fi");
        builder.AppendLine("/bin/sleep 0.3");
        builder.AppendLine("/sbin/pfctl -a com.apple/tidegate -F all >/dev/null 2>&1 || true");
        AppendPipeDeletes(builder);
        builder.AppendLine("/bin/rm -f \"$DIR/active.pipes\" \"$DIR/seen.tuples\" \"$DIR/live.status\"");
        builder.AppendLine("if [[ -f \"$TOKEN_FILE\" ]]; then");
        builder.AppendLine("  token=$(/bin/cat \"$TOKEN_FILE\")");
        builder.AppendLine("  /sbin/pfctl -X \"$token\" >/dev/null 2>&1 || true");
        builder.AppendLine("  /bin/rm -f \"$TOKEN_FILE\"");
        builder.AppendLine("fi");
        builder.AppendLine("echo \"已清除潮閘上限\"");
        return builder.ToString();
    }

    private const string HelperBody =
        """
        #!/bin/bash
        set -u
        DIR="$1"
        exec >>"$DIR/helper.log" 2>&1
        echo $$ > "$DIR/helper.pid"
        RULES="$DIR/rules.pf"
        PIPES="$DIR/pipes.txt"
        STATUS="$DIR/live.status"
        ACTIVE="$DIR/active.pipes"
        in_range() {
          local n="$1"
          if (( n >= 1001 && n <= 1199 )); then return 0; fi
          if (( n >= 2001 && n <= 2199 )); then return 0; fi
          return 1
        }
        apply_files() {
          local line num kbit
          while IFS= read -r line || [[ -n "$line" ]]; do
            [[ -z "$line" ]] && continue
            if [[ ! "$line" =~ ^dummynet\ (in|out)\ quick\ (inet|inet6)\ proto\ (tcp|udp)\ from\ [0-9A-Fa-f.:]+\ port\ [0-9]+\ to\ [0-9A-Fa-f.:]+\ port\ [0-9]+\ allow-opts\ pipe\ [0-9]+$ ]]; then
              printf '%s\n' "規則內容不符合潮閘格式" > "$STATUS"
              return 1
            fi
            num="${line##* pipe }"
            if ! in_range "$num"; then
              printf '%s\n' "規則使用了保留範圍外的管線" > "$STATUS"
              return 1
            fi
          done < "$RULES"
          while IFS= read -r line || [[ -n "$line" ]]; do
            [[ -z "$line" ]] && continue
            if [[ ! "$line" =~ ^[0-9]+\ [0-9]+$ ]]; then
              printf '%s\n' "管線內容不符合潮閘格式" > "$STATUS"
              return 1
            fi
            num="${line%% *}"
            kbit="${line##* }"
            if ! in_range "$num" || (( kbit < 1 || kbit > 80000000 )); then
              printf '%s\n' "管線編號或頻寬超出範圍" > "$STATUS"
              return 1
            fi
          done < "$PIPES"
          while IFS= read -r line || [[ -n "$line" ]]; do
            [[ -z "$line" ]] && continue
            num="${line%% *}"
            kbit="${line##* }"
            if ! /usr/sbin/dnctl pipe "$num" config bw "${kbit}Kbit/s"; then
              printf '%s\n' "dnctl 無法建立管線 $num" > "$STATUS"
              return 1
            fi
          done < "$PIPES"
          if [[ -f "$ACTIVE" ]]; then
            while IFS= read -r num || [[ -n "$num" ]]; do
              [[ -z "$num" ]] && continue
              if ! /usr/bin/grep -q "^${num} " "$PIPES"; then
                /usr/sbin/dnctl pipe "$num" delete >/dev/null 2>&1 || true
              fi
            done < "$ACTIVE"
          fi
          /usr/bin/awk '{print $1}' "$PIPES" > "$ACTIVE"
          if ! /sbin/pfctl -a com.apple/tidegate -f "$RULES"; then
            printf '%s\n' "pfctl 拒絕載入潮閘規則" > "$STATUS"
            return 1
          fi
          while IFS= read -r line || [[ -n "$line" ]]; do
            [[ -z "$line" ]] && continue
            if [[ "$line" =~ from\ ([0-9A-Fa-f.:]+)\ port\ [0-9]+\ to\ ([0-9A-Fa-f.:]+)\ port\ [0-9]+ ]]; then
              /sbin/pfctl -k "${BASH_REMATCH[1]}" -k "${BASH_REMATCH[2]}" >/dev/null 2>&1 || true
            fi
          done < "$RULES"
          printf '%s\n' "已套用潮閘上限。新的連線會自動補進同一條管線。" > "$STATUS"
        }
        last=""
        while [[ -f "$DIR/helper.on" ]]; do
          if [[ -f "$RULES" && -f "$PIPES" ]]; then
            sig="$(/usr/bin/stat -f %m "$RULES")-$(/usr/bin/stat -f %m "$PIPES")"
            if [[ "$sig" != "$last" ]]; then
              if apply_files; then
                last="$sig"
              fi
            fi
          fi
          /bin/sleep 1
        done
        """;

    public static string BashQuote(string path)
    {
        if (path.Length == 0 || path.IndexOf('\0') >= 0 || path.Contains('\n') || path.Contains('\r'))
            throw new ArgumentException("路徑不能是空的，也不能含有換行。", nameof(path));
        return "'" + path.Replace("'", "'\\''") + "'";
    }

    private static void AppendPipeDeletes(StringBuilder builder)
    {
        builder.AppendLine($"for i in $(/usr/bin/seq {LimitPlanner.DownloadPipeBase} {LimitPlanner.DownloadPipeBase + LimitPlanner.MaxApps}); do");
        builder.AppendLine("  /usr/sbin/dnctl pipe \"$i\" delete >/dev/null 2>&1 || true");
        builder.AppendLine("done");
        builder.AppendLine($"for i in $(/usr/bin/seq {LimitPlanner.UploadPipeBase} {LimitPlanner.UploadPipeBase + LimitPlanner.MaxApps}); do");
        builder.AppendLine("  /usr/sbin/dnctl pipe \"$i\" delete >/dev/null 2>&1 || true");
        builder.AppendLine("done");
    }

    private static void ValidatePipes(IReadOnlyList<PipeSpec> pipes)
    {
        foreach (var pipe in pipes)
        {
            var download = pipe.Number - LimitPlanner.DownloadPipeBase;
            var upload = pipe.Number - LimitPlanner.UploadPipeBase;
            var inDownload = download is >= 1 and <= LimitPlanner.MaxApps;
            var inUpload = upload is >= 1 and <= LimitPlanner.MaxApps;
            if (!inDownload && !inUpload)
                throw new ArgumentOutOfRangeException(nameof(pipes), "管線編號超出潮閘保留範圍。");
            if (pipe.BitsPerSecond < 1)
                throw new ArgumentOutOfRangeException(nameof(pipes), "管線頻寬必須大於 0。");
        }
    }
}
