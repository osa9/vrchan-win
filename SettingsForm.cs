using System;
using System.Windows.Forms;

public class SettingsForm : Form
{
    private TextBox txtVrcUsername;
    private TextBox txtVrcPassword;
    private TextBox txtVrcGroupId;
    private TextBox txtTotpSecret;
    private TextBox txtDiscordWebhook;
    private NumericUpDown numIntervalMinutes;
    private LinkLabel lnkDiscordWebhookDoc;
    private Button btnSave;
    private Button btnCancel;

    private readonly AppConfig _config;

    public SettingsForm(AppConfig config)
    {
        _config = config;

        InitializeComponent();

        // 現在の設定を反映
        txtVrcUsername.Text = _config.VrcUsername;
        txtVrcPassword.Text = _config.VrcPassword;
        txtVrcGroupId.Text = _config.VrcGroupId;
        txtTotpSecret.Text = _config.TotpSecret;
        txtDiscordWebhook.Text = _config.DiscordWebhookUrl;
        numIntervalMinutes.Value = _config.IntervalMinutes <= 0 ? 5 : _config.IntervalMinutes;
    }

    private void InitializeComponent()
    {
        this.Text = "VRChan 設定";
        this.Width = 600;
        this.Height = 520;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        int labelWidth = 220;
        int controlWidth = 520;
        int left = 10;
        int top = 20;
        int lineHeight = 28;
        int labelHeight = 24;

        var lblVrcUser = new Label
        {
            Text = "VRChat ユーザー名:",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        txtVrcUsername = new TextBox
        {
            Left = left,
            Top = top + labelHeight,
            Width = controlWidth
        };

        top += lineHeight * 2;

        var lblVrcPass = new Label
        {
            Text = "VRChat パスワード:",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        txtVrcPassword = new TextBox
        {
            Left = left,
            Top = top + labelHeight,
            Width = controlWidth,
            UseSystemPasswordChar = true
        };

        top += lineHeight * 2;

        var lblGroupId = new Label
        {
            Text = "VRChat グループID:",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        txtVrcGroupId = new TextBox
        {
            Left = left,
            Top = top + labelHeight,
            Width = controlWidth
        };

        top += lineHeight * 2;

        var lblTotp = new Label
        {
            Text = "VRChat TOTP シークレット(Base32):",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        txtTotpSecret = new TextBox
        {
            Left = left,
            Top = top + labelHeight,
            Width = controlWidth
        };

        top += lineHeight * 2;

        var lblDiscord = new Label
        {
            Text = "Discord Webhook URL:",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        txtDiscordWebhook = new TextBox
        {
            Left = left,
            Top = top + labelHeight,
            Width = controlWidth
        };

        lnkDiscordWebhookDoc = new LinkLabel
        {
            Text = "Webhook の作成方法 (ブラウザで開く)",
            Left = left,
            Top = top + labelHeight + 26,
            Width = controlWidth
        };
        lnkDiscordWebhookDoc.LinkClicked += (s, e) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://support.discord.com/hc/ja/articles/228383668",
                UseShellExecute = true
            });
        };

        top += lineHeight * 2 + 24; // Discord説明リンク分も考慮して少し余白を増やす

        var lblInterval = new Label
        {
            Text = "監視間隔(分):",
            Left = left,
            Top = top,
            Width = labelWidth
        };
        numIntervalMinutes = new NumericUpDown
        {
            Left = left,
            Top = top + 18,
            Width = 80,
            Minimum = 1,
            Maximum = 1440
        };

        // ボタンはフォーム下部に固定配置
        // InitializeComponent 実行時点では ClientSize が 0 の場合があるので、
        // 想定値から逆算した位置に配置する。
        int buttonsTop = 400;

        btnSave = new Button
        {
            Text = "保存",
            Left = this.ClientSize.Width - 180,
            Top = buttonsTop,
            Width = 80,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnSave.Click += BtnSave_Click;

        btnCancel = new Button
        {
            Text = "キャンセル",
            Left = this.ClientSize.Width - 90,
            Top = buttonsTop,
            Width = 80,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnCancel.Click += (s, e) => this.Close();

        this.Controls.Add(lblVrcUser);
        this.Controls.Add(txtVrcUsername);
        this.Controls.Add(lblVrcPass);
        this.Controls.Add(txtVrcPassword);
        this.Controls.Add(lblGroupId);
        this.Controls.Add(txtVrcGroupId);
        this.Controls.Add(lblTotp);
        this.Controls.Add(txtTotpSecret);
        this.Controls.Add(lblDiscord);
        this.Controls.Add(txtDiscordWebhook);
        this.Controls.Add(lnkDiscordWebhookDoc);
        this.Controls.Add(lblInterval);
        this.Controls.Add(numIntervalMinutes);
        this.Controls.Add(btnSave);
        this.Controls.Add(btnCancel);
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtVrcUsername.Text))
        {
            MessageBox.Show("VRChatのユーザー名を入力してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(txtVrcPassword.Text))
        {
            MessageBox.Show("VRChatのパスワードを入力してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(txtVrcGroupId.Text))
        {
            MessageBox.Show("VRChatのグループIDを入力してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(txtTotpSecret.Text))
        {
            MessageBox.Show("VRChatのTOTPシークレットを入力してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(txtDiscordWebhook.Text))
        {
            MessageBox.Show("DiscordのWebhook URLを入力してください。");
            return;
        }

        _config.VrcUsername = txtVrcUsername.Text.Trim();
        _config.VrcPassword = txtVrcPassword.Text.Trim();
        _config.VrcGroupId = txtVrcGroupId.Text.Trim();
        _config.TotpSecret = txtTotpSecret.Text.Trim();
        _config.DiscordWebhookUrl = txtDiscordWebhook.Text.Trim();
        _config.IntervalMinutes = (int)numIntervalMinutes.Value;
        _config.Save();

        this.DialogResult = DialogResult.OK;
        this.Close();
    }
}
