using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CSL.IfeoRedirector;

/// <summary>
/// WDAC（Windows Defender Application Control）风格的阻止提示弹窗（本地降级用）。
///
/// 正常情况下弹窗由主程序（SYSTEM + UIAccess）显示，可覆盖锁屏遮罩等一切窗口；
/// 本类仅在主程序不可达（未运行 / 重启中）时降级使用：
/// 全屏深色遮罩 rgba(0,0,0,0.5) + TopMost 置顶蓝色卡片，尽可能接近 WDAC 效果。
///
/// 视觉规范（对照 WDAC 弹窗 HTML/CSS）：
///   遮罩 rgba(0,0,0,0.5)；卡片背景 #005A9E、宽 850、圆角 4、
///   阴影 0 4px 16px rgba(0,0,0,0.15)；标题 21px、正文/路径 15px（Segoe UI，
///   中文回退微软雅黑）；按钮白色 1px 描边 + hover 半透明白 rgba(255,255,255,0.1)；
///   路径行显示被拦截应用的完整路径。
/// </summary>
internal sealed class WdacBlockDialog : Form
{
    private const int WindowWidth = 850;
    private const int WindowHeight = 292;
    private const int CornerRadius = 4;
    private const int SidePadding = 40;
    private const int TopPadding = 35;
    private const int ButtonHeight = 36;
    private const int ButtonGap = 12;
    private const int ContentWidth = WindowWidth - SidePadding * 2; // 560

    private static readonly Color WindowsBlue = Color.FromArgb(0x00, 0x5A, 0x9E);
    private static readonly Font TitleFont = new("Segoe UI", 21f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font BodyFont = new("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);

    private readonly Button _copyButton;
    private readonly string _appName;
    private readonly string _appPath;
    private Timer? _restoreTimer;

    private WdacBlockDialog(string appName, string appPath)
    {
        _appName = appName;
        _appPath = appPath;

        FormBorderStyle = FormBorderStyle.None;              // 无边框 + 自绘内容区
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;                                      // 本地降级弹窗也置顶，避免被普通窗口盖住
        BackColor = WindowsBlue;
        ClientSize = new Size(WindowWidth, WindowHeight);
        Font = BodyFont;
        ShowInTaskbar = false;

        // 标题两行（与 WDAC 文案结构一致）
        var title1 = MakeLabel("你的组织使用了 ClassScreenLock 应用程序控制", TitleFont,
            SidePadding, TopPadding);
        var title2 = MakeLabel("来阻止此应用", TitleFont,
            SidePadding, title1.Bottom + 5);

        // 被拦截路径（15px，上 20 下 25；超宽时省略号截断）
        var path = MakeLabel(_appPath, BodyFont, SidePadding, title2.Bottom + 20, ContentWidth, true);

        // 描述正文（上 25）
        var desc = MakeLabel("有关详细信息，请与你的支持人员或管理人员联系。", BodyFont,
            SidePadding, path.Bottom + 25);

        // 底部按钮：右对齐（关闭在右，复制在左，间距 12px）
        var buttonTop = desc.Bottom + 35;
        var right = WindowWidth - SidePadding;

        var closeButton = MakeButton("关闭", 78);
        closeButton.Location = new Point(right - closeButton.Width, buttonTop);
        closeButton.Click += (_, _) => Close();

        _copyButton = MakeButton("复制到剪贴板", 150);
        _copyButton.Location = new Point(closeButton.Left - ButtonGap - _copyButton.Width, buttonTop);
        _copyButton.Click += (_, _) => CopyBlockedInfo();

        Controls.AddRange(new Control[] { title1, title2, path, desc, _copyButton, closeButton });
        CancelButton = closeButton; // 按 ESC 等同"关闭"
    }

    /// <summary>创建白色文字、按文本内容定宽的标签（尺寸立即可用，便于后续控件定位）。</summary>
    private static Label MakeLabel(string text, Font font, int x, int y)
    {
        return MakeLabel(text, font, x, y, int.MaxValue, false);
    }

    /// <summary>创建标签；文本超过 maxWidth 时压缩为固定宽（可选省略号截断）。</summary>
    private static Label MakeLabel(string text, Font font, int x, int y, int maxWidth, bool ellipsis)
    {
        var size = TextRenderer.MeasureText(text, font);
        var label = new Label
        {
            Text = text,
            Font = font,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            AutoSize = false,
            AutoEllipsis = ellipsis,
            Size = size.Width <= maxWidth ? size : new Size(maxWidth, size.Height),
            Location = new Point(x, y)
        };
        return label;
    }

    /// <summary>创建 WDAC 风格的白色描边按钮（透明底、hover 半透明白）。</summary>
    private static Button MakeButton(string text, int width)
    {
        var button = new Button
        {
            Text = text,
            Font = BodyFont,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(width, ButtonHeight),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.White;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(26, 255, 255, 255); // rgba(255,255,255,0.1)
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(51, 255, 255, 255); // 按下稍深
        return button;
    }

    /// <summary>把被阻止应用信息复制到剪贴板，并给按钮短暂的"已复制"反馈。</summary>
    private void CopyBlockedInfo()
    {
        var info = string.Join(Environment.NewLine,
            $"被阻止的应用：{_appName}",
            $"应用路径：{_appPath}",
            $"阻止时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "",
            "你的组织使用了 ClassScreenLock 应用程序控制来阻止此应用。",
            "有关详细信息，请与你的支持人员或管理人员联系。");

        if (!TrySetClipboard(info)) return;

        _copyButton.Text = "已复制";
        _restoreTimer?.Stop();
        _restoreTimer = new Timer { Interval = 2000 };
        _restoreTimer.Tick += (_, _) =>
        {
            _restoreTimer!.Stop();
            _copyButton.Text = "复制到剪贴板";
        };
        _restoreTimer.Start();
    }

    /// <summary>带轻量重试的剪贴板写入（剪贴板可能被其他进程短暂占用）。</summary>
    private static bool TrySetClipboard(string text)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, true, 10, 50);
                return true;
            }
            catch
            {
                System.Threading.Thread.Sleep(60);
            }
        }
        return false;
    }

    /// <summary>无边框窗口的原生投影（WS_EX_DROPSHADOW，Win10/11 DWM 提供）。</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00020000; // WS_EX_DROPSHADOW
            return cp;
        }
    }

    /// <summary>窗口句柄建立后应用圆角（Region 裁剪）。</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedCorners(CornerRadius);
    }

    private void ApplyRoundedCorners(int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(Width - d, 0, d, d, 270, 90);
        path.AddArc(Width - d, Height - d, d, d, 0, 90);
        path.AddArc(0, Height - d, d, d, 90, 90);
        path.CloseFigure();
        Region = new Region(path);
    }

    /// <summary>
    /// 模态显示阻止提示：全屏半透明遮罩 + 置顶弹窗。
    /// 调用方必须处于 STA 线程（[STAThread] Main）。
    /// </summary>
    public static void ShowBlockDialog(string appName, string appPath)
    {
        using var overlay = BuildOverlay();
        overlay.Show(); // 遮罩先显示（位于弹窗下层）

        using var dialog = new WdacBlockDialog(appName, appPath);
        dialog.ShowDialog(); // 弹窗后置顶，盖在遮罩之上

        overlay.Close();
    }

    /// <summary>全屏深色半透明遮罩：rgba(0,0,0,0.5)。</summary>
    private static Form BuildOverlay()
    {
        return new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen,
            BackColor = Color.Black,
            Opacity = 0.5,
            ShowInTaskbar = false,
            TopMost = true
        };
    }
}
