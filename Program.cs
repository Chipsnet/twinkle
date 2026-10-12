namespace Twinkle;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 動作確認用。exe に --selftest を付けて実行すると、電源APIだけを試して結果を表示する
        if (args.Contains("--selftest"))
        {
            SelfTest.Run(pause: args.Contains("--pause"));
            return;
        }

        // 2つ起動すると、元の設定値の保存と復元が衝突するため1つに制限する
        using var mutex = new Mutex(initiallyOwned: true, @"Local\twinkle", out var created);
        if (!created)
        {
            MessageBox.Show("twinkle は既に起動しています。", "twinkle");
            return;
        }

        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new TrayApp());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "twinkle");
        }
    }
}
