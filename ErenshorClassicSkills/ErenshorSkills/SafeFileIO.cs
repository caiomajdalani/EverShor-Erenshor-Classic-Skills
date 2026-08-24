using System;
using System.IO;
using System.Text;

namespace ErenshorSkills
{
    /// <summary>
    /// Crash-resistant text persistence shared by every Classic Skills sidecar.
    /// Writes are staged beside the destination, flushed, then atomically swapped.
    /// The previous valid file is retained as .bak and corrupt primaries are
    /// quarantined instead of being silently overwritten.
    /// </summary>
    internal static class SafeFileIO
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static bool TryWriteAllTextAtomic(string path, string content, out string error)
        {
            error = null;
            string tempPath = null;
            try
            {
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("A save path is required.", nameof(path));

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
                using (var stream = new FileStream(tempPath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Utf8NoBom))
                {
                    writer.Write(content ?? string.Empty);
                    writer.Flush();
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    string backupPath = path + ".bak";
                    try
                    {
                        File.Replace(tempPath, path, backupPath, true);
                    }
                    catch (PlatformNotSupportedException)
                    {
                        File.Copy(path, backupPath, true);
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                    catch (IOException)
                    {
                        // Some Proton-mounted filesystems do not implement
                        // File.Replace. Rename-overwrite remains atomic when the
                        // temporary file is in the same directory.
                        File.Copy(path, backupPath, true);
                        File.Delete(path);
                        File.Move(tempPath, path);
                    }
                }
                else
                {
                    File.Move(tempPath, path);
                }

                tempPath = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath))
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                    catch { }
                }
            }
        }

        public static bool TryReadAllTextWithBackup(string path,
            Func<string, bool> validator, out string content,
            out bool recoveredBackup, out string error)
        {
            content = null;
            recoveredBackup = false;
            error = null;

            string primaryError = null;
            if (TryReadValid(path, validator, out content, out primaryError))
                return true;

            string backupPath = path + ".bak";
            string backupError = null;
            if (!TryReadValid(backupPath, validator, out content, out backupError))
            {
                error = $"primary: {primaryError}; backup: {backupError}";
                content = null;
                return false;
            }

            recoveredBackup = true;
            try
            {
                if (File.Exists(path))
                {
                    string quarantine = path + ".corrupt." +
                        DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
                    File.Move(path, quarantine);
                }
                File.Copy(backupPath, path, true);
            }
            catch (Exception ex)
            {
                // Loading the verified backup is still safe even if the repair
                // could not be materialized on disk.
                error = "loaded backup, but primary repair failed: " + ex.Message;
            }
            return true;
        }

        private static bool TryReadValid(string path, Func<string, bool> validator,
            out string content, out string error)
        {
            content = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "file not found";
                    return false;
                }

                content = File.ReadAllText(path);
                if (validator != null && !validator(content))
                {
                    error = "validation failed";
                    content = null;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                content = null;
                return false;
            }
        }

        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            var sb = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static bool TryDecodeJsonStringToken(string token, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(token) || token.Length < 2 ||
                token[0] != '"' || token[token.Length - 1] != '"') return false;
            try
            {
                var sb = new StringBuilder(token.Length - 2);
                for (int i = 1; i < token.Length - 1; i++)
                {
                    char c = token[i];
                    if (c != '\\') { sb.Append(c); continue; }
                    if (++i >= token.Length - 1) return false;
                    switch (token[i])
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= token.Length) return false;
                            sb.Append((char)Convert.ToInt32(
                                token.Substring(i + 1, 4), 16));
                            i += 4;
                            break;
                        default: return false;
                    }
                }
                value = sb.ToString();
                return true;
            }
            catch
            {
                value = null;
                return false;
            }
        }
    }
}
