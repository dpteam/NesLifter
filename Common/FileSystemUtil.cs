using System.IO;
using System.Text;

namespace NesLifter.Common;

public static class FileSystemUtil
{
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "rom";

        StringBuilder sb = new StringBuilder();
        char[] invalid = Path.GetInvalidFileNameChars();

        foreach (char c in name)
        {
            if (Array.IndexOf(invalid, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }

        string result = sb.ToString().Trim();

        if (result.Length == 0)
            return "rom";

        return result;
    }

    public static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Game";

        StringBuilder sb = new StringBuilder();

        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }

        if (sb.Length == 0)
            sb.Append("Game");

        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        return sb.ToString();
    }
}