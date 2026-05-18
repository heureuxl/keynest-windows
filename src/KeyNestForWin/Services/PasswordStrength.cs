using System.Text.RegularExpressions;

namespace KeyNestForWin.Services;

public static class PasswordStrength
{
    /// <summary>本地启发式：过短、字符类过少视为弱密码。</summary>
    public static bool IsWeak(string password)
    {
        if (string.IsNullOrEmpty(password)) return true;
        if (password.Length < 8) return true;
        var classes = 0;
        if (Regex.IsMatch(password, "[a-z]")) classes++;
        if (Regex.IsMatch(password, "[A-Z]")) classes++;
        if (Regex.IsMatch(password, "[0-9]")) classes++;
        if (Regex.IsMatch(password, "[^a-zA-Z0-9]")) classes++;
        return classes < 2;
    }
}
