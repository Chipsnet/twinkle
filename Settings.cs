using System.Text.Json;
using Microsoft.Win32;

namespace Twinkle;

sealed class Settings
{
    public bool ApplyAc { get; set; } = true;
    public bool ApplyDc { get; set; }
    public bool KeepDisplay { get; set; }

    /// <summary>null 以外なら「カバー閉の設定を書き換えている最中」。</summary>
    public SavedLid? Saved { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "twinkle", "settings.json");

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                return new Settings();
            }
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    /// <summary>一時ファイルに書いてから差し替える（元の設定値を失わないため）。</summary>
    public void Save()
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Options));
        File.Move(tmp, path, overwrite: true);
    }
}

sealed record SavedLid(Guid Scheme, uint? Ac, uint? Dc);

/// <summary>HKCU の Run キーで自動起動を管理する。状態はレジストリだけを正とする。</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "twinkle";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
