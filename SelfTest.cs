namespace Twinkle;

/// <summary>Windows の電源APIが期待どおり動くかを確認する。-publish した exe では --selftest で実行する。</summary>
static class SelfTest
{
    public static void Run(bool pause = false)
    {
        var ok = true;
        ok &= Check("ActiveScheme", () =>
        {
            var scheme = Lid.ActiveScheme();
            Console.WriteLine($"  アクティブな電源プラン: {scheme}");
        });

        ok &= Check("LIDACTION の読み書き (AC/DC)", () =>
        {
            var scheme = Lid.ActiveScheme();
            var acBefore = Lid.Read(scheme, ac: true);
            var dcBefore = Lid.Read(scheme, ac: false);
            Console.WriteLine($"  書き換え前: AC={acBefore} DC={dcBefore}");

            Lid.Write(scheme, ac: true, Lid.DoNothing);
            Lid.Write(scheme, ac: false, Lid.DoNothing);
            Lid.Activate();
            Console.WriteLine($"  書き換え後: AC={Lid.Read(scheme, ac: true)} DC={Lid.Read(scheme, ac: false)} (期待値 0/0)");

            Lid.Write(scheme, ac: true, acBefore);
            Lid.Write(scheme, ac: false, dcBefore);
            Lid.Activate();
            var acAfter = Lid.Read(scheme, ac: true);
            var dcAfter = Lid.Read(scheme, ac: false);
            Console.WriteLine($"  書き戻し後: AC={acAfter} DC={dcAfter} (期待値 {acBefore}/{dcBefore})");

            if (acAfter != acBefore || dcAfter != dcBefore)
            {
                throw new InvalidOperationException("元の値に戻せませんでした");
            }
        });

        ok &= Check("スリープ防止 (PowerRequest)", () =>
        {
            using var awake = new Awake();
            awake.Set(system: true, display: false);
            Console.WriteLine("  PowerRequestSystemRequired を設定しました");
            Console.WriteLine("  (別のウィンドウで powercfg /requests を実行すると 'twinkle keep awake' が見えます)");
            awake.Set(system: true, display: true);
            Console.WriteLine("  PowerRequestDisplayRequired も設定しました");
            if (pause)
            {
                Console.WriteLine("  --pause のため15秒間そのままにします...");
                Thread.Sleep(15000);
            }
            awake.Set(system: false, display: false);
            Console.WriteLine("  解除しました");
        });

        Console.WriteLine(ok ? "すべて成功しました。" : "失敗があります。");
        Environment.Exit(ok ? 0 : 1);
    }

    static bool Check(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"[OK] {name}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NG] {name}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
