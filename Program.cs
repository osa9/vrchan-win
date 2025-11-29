namespace VrchanWin;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Toolkit.Uwp.Notifications;
using OtpNet;

internal static class Program
{
    private static AppConfig _config = AppConfig.Load();

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();

        // 起動時、設定が空なら設定画面を出す
        if (!_config.IsValid())
        {
            using var settings = new SettingsForm(_config);
            settings.ShowDialog();
        }

        // タスクトレイアイコン（アプリケーションの既定アイコンを使用）
        Icon? appIcon = null;
        try
        {
            // ApplicationIcon に設定された EXE のアイコンを取得
            var exePath = Application.ExecutablePath;
            appIcon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            // 失敗した場合は null のままにしておき、後でフォールバック
        }

        var tray = new NotifyIcon
        {
            Icon = appIcon ?? SystemIcons.Application,
            Visible = true,
            Text = "VRChan Tray"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("設定...", null, (s, e) =>
        {
            using var settings = new SettingsForm(_config);
            if (settings.ShowDialog() == DialogResult.OK)
            {
                // 設定更新後、必要なら監視ロジックに反映
                GroupInstanceWatcher.UpdateConfig(_config);
            }
        });
        menu.Items.Add("ログ...", null, (s, e) =>
        {
            using var logForm = new LogForm();
            logForm.ShowDialog();
        });
        menu.Items.Add("今すぐチェック", null, async (s, e) =>
        {
            await GroupInstanceWatcher.CheckOnceAndNotifyAsync();
        });
        menu.Items.Add("終了", null, (s, e) => Application.Exit());
        tray.ContextMenuStrip = menu;

        // バックグラウンド監視開始
        GroupInstanceWatcher.Start(_config);

        Application.Run();

        tray.Visible = false;
        GroupInstanceWatcher.Stop();
    }
}

// ==== グループインスタンス監視ロジックの骨組み ====

public static class GroupInstanceWatcher
{
    private static readonly CancellationTokenSource Cts = new();
    // インスタンスIDごとの最終通知時刻（ローカル時刻）
    private static readonly Dictionary<string, DateTime> NotifiedInstances = new();

    private static bool _running = false;
    private static AppConfig _config = new AppConfig();

    private static readonly HttpClient Http = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static void Start(AppConfig config)
    {
        _config = config;
        LoadNotifiedIds();
        if (_running) return;
        _running = true;
        _ = Task.Run(RunLoopAsync);
    }

    public static void UpdateConfig(AppConfig config)
    {
        _config = config;
    }

    public static void Stop()
    {
        Cts.Cancel();
        SaveNotifiedIds();
    }

    private static async Task RunLoopAsync()
    {
        while (!Cts.IsCancellationRequested)
        {
            try
            {
                if (_config.IsValid())
                {
                    await CheckOnceAndNotifyAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RunLoopAsync", ex);
            }

            try
            {
                var interval = _config.IntervalMinutes <= 0 ? 5 : _config.IntervalMinutes;
                await Task.Delay(TimeSpan.FromMinutes(interval), Cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public static async Task CheckOnceAndNotifyAsync()
    {
        if (!_config.IsValid())
        {
            Logger.Warn("CheckOnceAndNotifyAsync called but config is invalid.");
            return;
        }

        Logger.Info("Checking group instances...");
        var instances = await FetchGroupInstancesAsync();

        if (instances.Count == 0)
        {
            Logger.Info("No instances found.");
            return;
        }

        var now = DateTime.Now;

        // まず「新規インスタンス」を全て通知
        foreach (var instance in instances)
        {
            if (!NotifiedInstances.ContainsKey(instance.Id))
            {
                Logger.Info($"New instance: {instance.Id} {instance.WorldName} ({instance.InstanceUrl})");

                ShowToast(instance);
                await SendDiscordAsync(instance);

                NotifiedInstances[instance.Id] = now;
            }
        }

        // 既知インスタンスについて、4時間以上経過しているもののうち
        // 最終通知時刻が最も古いものを1つだけ再通知する
        var fourHoursAgo = now.AddHours(-4);
        GroupInstance? candidate = null;
        DateTime oldestTime = DateTime.MaxValue;

        foreach (var instance in instances)
        {
            if (!NotifiedInstances.TryGetValue(instance.Id, out var lastNotified))
            {
                continue;
            }

            if (lastNotified <= fourHoursAgo && lastNotified < oldestTime)
            {
                oldestTime = lastNotified;
                candidate = instance;
            }
        }

        if (candidate != null)
        {
            Logger.Info($"Re-notify instance after 4+ hours: {candidate.Id} {candidate.WorldName} ({candidate.InstanceUrl})");
            ShowToast(candidate);
            await SendDiscordAsync(candidate);
            NotifiedInstances[candidate.Id] = now;
        }
    }

    private static async Task<List<GroupInstance>> FetchGroupInstancesAsync()
    {
        // ここでは Python 実装のうち group_instances + TOTP ログインを簡易移植する

        Logger.Info("Logging in to VRChat API...");

        using var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer()
        };

        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.vrchat.cloud/api/1/")
        };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VrchanWin", "1.0"));

        // Basic 認証で /auth/user を叩く（2FAが必要な場合は TOTP を送信）
        var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_config.VrcUsername}:{_config.VrcPassword}"));
        var req = new HttpRequestMessage(HttpMethod.Get, "auth/user");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var res = await client.SendAsync(req);
        if (!res.IsSuccessStatusCode)
        {
            throw new Exception($"VRChat login failed: {(int)res.StatusCode}");
        }

        var body = await res.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("requiresTwoFactorAuth", out var tfaElement) &&
            tfaElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var needsTotp = false;
            foreach (var v in tfaElement.EnumerateArray())
            {
                if (string.Equals(v.GetString(), "totp", StringComparison.OrdinalIgnoreCase))
                {
                    needsTotp = true;
                    break;
                }
            }

            if (needsTotp)
            {
                if (string.IsNullOrWhiteSpace(_config.TotpSecret))
                {
                    throw new Exception("VRChat requires TOTP but TotpSecret is not configured.");
                }

                var bytes = Base32Encoding.ToBytes(_config.TotpSecret.Trim());
                var totp = new Totp(bytes);
                var code = totp.ComputeTotp();

                var verifyPayload = new
                {
                    code
                };

                var jsonVerify = System.Text.Json.JsonSerializer.Serialize(verifyPayload);
                var content = new StringContent(jsonVerify, System.Text.Encoding.UTF8, "application/json");
                Logger.Info("Sending TOTP code to VRChat...");
                var verifyRes = await client.PostAsync("auth/twofactorauth/totp/verify", content);
                if (!verifyRes.IsSuccessStatusCode)
                {
                    var status = (int)verifyRes.StatusCode;
                    if (status == 429)
                    {
                        Logger.Error("TOTP verify failed with 429 (Too Many Requests). 認証試行回数が多すぎる可能性があります。しばらく時間をおいてから再度お試しください。");
                    }
                    throw new Exception($"TOTP verify failed: {status}");
                }
            }
        }

        // グループインスタンス一覧を取得
        Logger.Info($"Fetching group instances for group {_config.VrcGroupId}...");
        var instancesRes = await client.GetAsync($"groups/{_config.VrcGroupId}/instances");
        if (!instancesRes.IsSuccessStatusCode)
        {
            throw new Exception($"get_group_instances failed: {(int)instancesRes.StatusCode}");
        }

        var json = await instancesRes.Content.ReadAsStringAsync();
        // 必要最低限の項目だけをDTOでパース
        var list = System.Text.Json.JsonSerializer.Deserialize<List<VrcInstanceDto>>(json) ?? new();

        var result = new List<GroupInstance>();
        foreach (var inst in list)
        {
            if (inst.world == null) continue;
            var world = inst.world;
            var worldId = world.id ?? "";
            var worldName = string.IsNullOrWhiteSpace(world.name) ? "(NO TITLE)" : world.name;
            var instanceId = inst.instanceId ?? "";
            var instanceUrl = $"https://vrchat.com/home/launch?worldId={worldId}&instanceId={instanceId}";
            var worldUrl = $"https://vrchat.com/home/launch?worldId={worldId}";

            DateTime? createdAtJst = null;
            if (!string.IsNullOrWhiteSpace(world.created_at) &&
                DateTime.TryParse(world.created_at, out var createdAtUtc))
            {
                // Python版と同様にJST(+9)に変換
                createdAtJst = createdAtUtc.ToUniversalTime().AddHours(9);
            }

            result.Add(new GroupInstance
            {
                Id = instanceId,
                WorldName = worldName,
                GroupName = _config.VrcGroupId,
                InstanceUrl = instanceUrl,
                WorldUrl = worldUrl,
                WorldDescription = world.description,
                ThumbnailImageUrl = world.thumbnailImageUrl,
                WorldCreatedAtJst = createdAtJst,
                Popularity = world.popularity,
                Favorites = world.favorites
            });
        }

        return result;
    }

    private static void ShowToast(GroupInstance instance)
    {
        // Microsoft.Toolkit.Uwp.Notifications v7.1.3 には Show 拡張がないため、
        // ここでは将来の拡張ポイントとして残し、実際の表示は省略する。
        // 必要であれば UWP アプリ側でのトースト実装に差し替えてください。
        Debug.WriteLine($"Toast: {instance.GroupName} - {instance.WorldName} ({instance.InstanceUrl})");
    }

    private static string GetStateFilePath()
    {
        // アプリケーション実行ファイルと同じディレクトリに保存
        var exeDir = System.IO.Path.GetDirectoryName(typeof(Program).Assembly.Location)
                   ?? Environment.CurrentDirectory;
        return System.IO.Path.Combine(exeDir, "notified_instances.json");
    }

    private static void LoadNotifiedIds()
    {
        try
        {
            var path = GetStateFilePath();
            if (!System.IO.File.Exists(path)) return;

            var json = System.IO.File.ReadAllText(path);
            NotifiedInstances.Clear();

            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new();
            foreach (var kv in dict)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                NotifiedInstances[kv.Key] = kv.Value;
            }
            Logger.Info($"Loaded {NotifiedInstances.Count} notified instances from state file.");
        }
        catch (Exception ex)
        {
            Logger.Error("LoadNotifiedIds failed", ex);
        }
    }

    private static void SaveNotifiedIds()
    {
        try
        {
            var path = GetStateFilePath();
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var json = System.Text.Json.JsonSerializer.Serialize(NotifiedInstances, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(path, json);
            Logger.Info($"Saved {NotifiedInstances.Count} notified instances to state file.");
        }
        catch (Exception ex)
        {
            Logger.Error("SaveNotifiedIds failed", ex);
        }
    }

    private static async Task SendDiscordAsync(GroupInstance instance)
    {
        if (string.IsNullOrWhiteSpace(_config.DiscordWebhookUrl))
        {
            return;
        }

        // Python版 notify_group_instances 相当のDiscordメッセージフォーマット
        // 本家は Redis に詳細を保存するが、ここでは通知内容のみを再現する。

        // VRChatのworld情報をログからしか保持していないため、
        // ここでは最低限の情報でembedを構成する。

        var embeds = new[]
        {
            new
            {
                title = instance.WorldName ?? "(NO TITLE)",
                url = instance.WorldUrl,
                description = instance.WorldDescription,
                image = string.IsNullOrWhiteSpace(instance.ThumbnailImageUrl)
                    ? null
                    : new { url = instance.ThumbnailImageUrl },
                fields = new object[]
                {
                    new
                    {
                        name = "ワールド公開日",
                        value = instance.WorldCreatedAtJst?.ToString("yyyy年MM月dd日") ?? "-",
                        inline = false
                    },
                    new
                    {
                        name = ":fire: Popularity",
                        value = instance.Popularity?.ToString() ?? "-",
                        inline = true
                    },
                    new
                    {
                        name = ":bookmark: Bookmarks",
                        value = instance.Favorites?.ToString() ?? "-",
                        inline = true
                    }
                }
            }
        };

        var components = new[]
        {
            new
            {
                type = 1,
                components = new[]
                {
                    new
                    {
                        type = 2,
                        style = 5,
                        url = instance.InstanceUrl,
                        label = "Launch Instance"
                    }
                }
            }
        };

        var payload = new
        {
            content = "グループインスタンス通知",
            embeds,
            components
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var res = await Http.PostAsync(_config.DiscordWebhookUrl, content);
        Logger.Info($"Discord webhook: {(int)res.StatusCode}");
    }

    private class GroupInstance
    {
        public string Id { get; set; } = "";
        public string? WorldName { get; set; }
        public string? GroupName { get; set; }
        public string? InstanceUrl { get; set; }
        public string? WorldUrl { get; set; }
        public string? WorldDescription { get; set; }
        public string? ThumbnailImageUrl { get; set; }
        public DateTime? WorldCreatedAtJst { get; set; }
        public int? Popularity { get; set; }
        public int? Favorites { get; set; }
    }

    private class VrcInstanceDto
    {
        public string? instanceId { get; set; }
        public WorldDto? world { get; set; }
    }

    private class WorldDto
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
        public string? thumbnailImageUrl { get; set; }
        public string? created_at { get; set; }
        public int? popularity { get; set; }
        public int? favorites { get; set; }
    }
}
