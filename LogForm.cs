using System;
using System.Linq;
using System.Windows.Forms;

namespace VrchanWin;

public class LogForm : Form
{
    private readonly TextBox _txt;
    private readonly Button _btnRefresh;

    public LogForm()
    {
        Text = "VRChan ログ";
        Width = 900;
        Height = 500;
        StartPosition = FormStartPosition.CenterParent;

        _txt = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9)
        };

        _btnRefresh = new Button
        {
            Text = "再読み込み",
            Dock = DockStyle.Bottom,
            Height = 32
        };
        _btnRefresh.Click += (_, _) => RefreshLog();

        Controls.Add(_txt);
        Controls.Add(_btnRefresh);

        Load += (_, _) => RefreshLog();
    }

    private void RefreshLog()
    {
        _txt.Lines = Logger.Lines;
        if (_txt.Lines.Length > 0)
        {
            _txt.SelectionStart = _txt.Text.Length;
            _txt.ScrollToCaret();
        }
    }
}
