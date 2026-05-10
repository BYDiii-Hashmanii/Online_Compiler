using System.Runtime.InteropServices;

namespace RemoteCompiler;

public class MainForm : Form
{
    // ── Engine instances ──────────────────────────────────────────────────────
    private CompilerServerEngine? _server;
    private CompilerClient?       _client;
    private CancellationTokenSource? _compileCts;

    // ── Palette ───────────────────────────────────────────────────────────────
    static readonly Color C_BG        = Color.FromArgb(18,  20,  28);
    static readonly Color C_SURFACE   = Color.FromArgb(28,  30,  40);
    static readonly Color C_PANEL     = Color.FromArgb(34,  36,  48);
    static readonly Color C_HEADER    = Color.FromArgb(38,  40,  54);
    static readonly Color C_BORDER    = Color.FromArgb(55,  58,  75);
    static readonly Color C_TEXT      = Color.FromArgb(215, 218, 230);
    static readonly Color C_MUTED     = Color.FromArgb(130, 134, 155);
    static readonly Color C_BLUE      = Color.FromArgb( 56, 139, 253);
    static readonly Color C_GREEN     = Color.FromArgb( 46, 184, 120);
    static readonly Color C_RED       = Color.FromArgb(220,  80,  80);
    static readonly Color C_AMBER     = Color.FromArgb(230, 160,  50);
    static readonly Color C_CODE_BG   = Color.FromArgb(13,  15,  22);
    static readonly Color C_KW        = Color.FromArgb( 86, 156, 214);
    static readonly Color C_STRING    = Color.FromArgb(206, 145, 120);
    static readonly Color C_COMMENT   = Color.FromArgb( 87, 166,  74);
    static readonly Color C_PREPROCESSOR = Color.FromArgb(155, 155, 100);

    // ── Server tab ───────────────────────────────────────────────────────────
    private RichTextBox    _serverLog      = null!;
    private Label          _lblSessions    = null!;
    private Button         _btnStart       = null!;
    private Button         _btnStop        = null!;
    private NumericUpDown  _nudPort        = null!;
    private NumericUpDown  _nudMaxConc     = null!;

    // ── Client tab ───────────────────────────────────────────────────────────
    private RichTextBox    _editor         = null!;
    private RichTextBox    _output         = null!;
    private TextBox        _txtIp          = null!;
    private NumericUpDown  _nudClientPort  = null!;
    private Button         _btnCompile     = null!;
    private Button         _btnCancel      = null!;
    private Label          _lblStatus      = null!;
    private ProgressBar    _progress       = null!;
    private Label          _lblLineCol     = null!;

    // ── Syntax highlight throttle ─────────────────────────────────────────────
    private System.Windows.Forms.Timer _hlTimer = null!;
    private bool _highlighting;

    // ─────────────────────────────────────────────────────────────────────────
    public MainForm()
    {
        SuspendLayout();
        BuildForm();
        BuildTabs();
        ResumeLayout();
        LoadDefaultCode();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  FORM SETUP
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildForm()
    {
        Text            = "Remote C++ Compiler";
        Size            = new Size(1100, 760);
        MinimumSize     = new Size(900,  620);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = C_BG;
        ForeColor       = C_TEXT;
        Font            = new Font("Segoe UI", 9.5f);
        Icon            = SystemIcons.Application;

        // Highlight throttle timer (fires 300ms after last keystroke)
        _hlTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _hlTimer.Tick += (_, __) => { _hlTimer.Stop(); ApplyHighlight(); };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  TABS
    // ═══════════════════════════════════════════════════════════════════════════
    private void BuildTabs()
    {
        var tabs = new TabControl
        {
            Dock      = DockStyle.Fill,
            Font      = new Font("Segoe UI", 10f),
            BackColor = C_BG,
        };
        tabs.TabPages.Add(BuildServerTab());
        tabs.TabPages.Add(BuildClientTab());
        Controls.Add(tabs);
    }

    // ── SERVER TAB ────────────────────────────────────────────────────────────
    private TabPage BuildServerTab()
    {
        var page = MakePage("⚙   Server");

        // ── Top toolbar ──
        var toolbar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 64,
            BackColor = C_PANEL,
            Padding   = new Padding(14, 0, 14, 0),
        };
        DrawBorder(toolbar, bottom: true);

        // Port
        toolbar.Controls.Add(MakeLabel("Port:", 14, 22));
        _nudPort = MakeNud(1024, 65535, 8888, 75, 14, 20);
        toolbar.Controls.Add(_nudPort);

        // Max concurrent
        toolbar.Controls.Add(MakeLabel("Max concurrent:", 106, 22));
        _nudMaxConc = MakeNud(1, 16, 4, 52, 240, 20);
        toolbar.Controls.Add(_nudMaxConc);

        // Buttons
        _btnStart = MakeButton("▶  Start", C_GREEN, 312, 16, 110, 30);
        _btnStart.Click += BtnStart_Click;
        toolbar.Controls.Add(_btnStart);

        _btnStop = MakeButton("■  Stop", C_RED, 432, 16, 110, 30);
        _btnStop.Enabled = false;
        _btnStop.Click  += BtnStop_Click;
        toolbar.Controls.Add(_btnStop);

        // Clear log
        var btnClear = MakeButton("Clear log", C_BORDER, 560, 16, 90, 30);
        btnClear.Click += (_, __) => _serverLog.Clear();
        toolbar.Controls.Add(btnClear);

        // Session counter
        _lblSessions = new Label
        {
            Text      = "● Active sessions: 0",
            ForeColor = C_MUTED,
            AutoSize  = true,
            Location  = new Point(670, 22),
        };
        toolbar.Controls.Add(_lblSessions);

        // ── Log box ──
        _serverLog = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = C_CODE_BG,
            ForeColor   = Color.FromArgb(140, 230, 160),
            Font        = new Font("Cascadia Code", 9f),
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            Padding     = new Padding(10),
        };

        page.Controls.Add(_serverLog);
        page.Controls.Add(toolbar);
        return page;
    }

    // ── CLIENT TAB ────────────────────────────────────────────────────────────
    private TabPage BuildClientTab()
    {
        var page = MakePage("  Client");

        // ── Top toolbar ──
        var toolbar = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 64,
            BackColor = C_PANEL,
            Padding   = new Padding(14, 0, 14, 0),
        };
        DrawBorder(toolbar, bottom: true);

        toolbar.Controls.Add(MakeLabel("Server IP:", 14, 22));
        _txtIp = new TextBox
        {
            Text        = "127.0.0.1",
            Location    = new Point(84, 18),
            Width       = 130,
            BackColor   = C_SURFACE,
            ForeColor   = C_TEXT,
            BorderStyle = BorderStyle.FixedSingle,
            Font        = Font,
        };
        toolbar.Controls.Add(_txtIp);

        toolbar.Controls.Add(MakeLabel("Port:", 228, 22));
        _nudClientPort = MakeNud(1024, 65535, 8888, 75, 266, 20);
        toolbar.Controls.Add(_nudClientPort);

        _btnCompile = MakeButton("▶  Compile & Run", C_BLUE, 360, 16, 150, 30);
        _btnCompile.Click += BtnCompile_Click;
        toolbar.Controls.Add(_btnCompile);

        _btnCancel = MakeButton("✕  Cancel", C_RED, 520, 16, 100, 30);
        _btnCancel.Enabled = false;
        _btnCancel.Click  += (_, __) => _compileCts?.Cancel();
        toolbar.Controls.Add(_btnCancel);

        _progress = new ProgressBar
        {
            Style    = ProgressBarStyle.Marquee,
            Location = new Point(634, 22),
            Width    = 160,
            Height   = 14,
            Visible  = false,
        };
        toolbar.Controls.Add(_progress);

        _lblStatus = new Label
        {
            Text      = "Ready",
            ForeColor = C_GREEN,
            AutoSize  = true,
            Location  = new Point(634, 22),
        };
        toolbar.Controls.Add(_lblStatus);

        // ── Split: editor | output ──
        var split = new SplitContainer
        {
            Dock        = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor   = C_BORDER,
        };
        // ALL splitter sizing deferred to after the form is fully shown
        split.SizeChanged += (_, __) =>
        {
            try
            {
                int minLeft  = 300;
                int minRight = 220;
                if (split.Width > minLeft + minRight + split.SplitterWidth)
                {
                    split.Panel1MinSize    = minLeft;
                    split.Panel2MinSize    = minRight;
                    split.SplitterDistance = Math.Max(minLeft,
                        Math.Min(split.Width - minRight - split.SplitterWidth,
                                 (int)(split.Width * 0.57)));
                    split.SizeChanged -= null; // run only once
                }
            }
            catch { /* ignore if still too small */ }
        };

        // Left: editor
        var edHeader = MakeHeaderBar("C++ Editor");
        var btnClrEd = MakeSmallButton("Clear");
        btnClrEd.Click += (_, __) => _editor.Clear();
        edHeader.Controls.Add(btnClrEd);

        _editor = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = C_CODE_BG,
            ForeColor   = C_TEXT,
            Font        = new Font("Cascadia Code", 10.5f),
            BorderStyle = BorderStyle.None,
            AcceptsTab  = true,
            DetectUrls  = false,
        };
        _editor.TextChanged  += Editor_TextChanged;
        _editor.SelectionChanged += (_, __) => UpdateLineCol();

        _lblLineCol = new Label
        {
            Dock      = DockStyle.Bottom,
            Height    = 22,
            BackColor = C_HEADER,
            ForeColor = C_MUTED,
            Text      = "Ln 1, Col 1",
            Font      = new Font("Segoe UI", 8.5f),
            Padding   = new Padding(8, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        leftPanel.Controls.Add(_editor);
        leftPanel.Controls.Add(edHeader);
        leftPanel.Controls.Add(_lblLineCol);
        split.Panel1.Controls.Add(leftPanel);

        // Right: output
        var outHeader = MakeHeaderBar("Output");
        var btnClrOut = MakeSmallButton("Clear");
        btnClrOut.Click += (_, __) => _output.Clear();
        outHeader.Controls.Add(btnClrOut);

        _output = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = C_CODE_BG,
            ForeColor   = Color.FromArgb(190, 230, 190),
            Font        = new Font("Cascadia Code", 10f),
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            DetectUrls  = false,
        };

        var rightPanel = new Panel { Dock = DockStyle.Fill };
        rightPanel.Controls.Add(_output);
        rightPanel.Controls.Add(outHeader);
        split.Panel2.Controls.Add(rightPanel);

        page.Controls.Add(split);
        page.Controls.Add(toolbar);
        return page;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ═══════════════════════════════════════════════════════════════════════════

    // ── Server ────────────────────────────────────────────────────────────────
    private void BtnStart_Click(object? s, EventArgs e)
    {
        try
        {
            _server = new CompilerServerEngine((int)_nudPort.Value, (int)_nudMaxConc.Value);

            _server.OnLog += msg => SafeInvoke(() =>
            {
                _serverLog.SelectionColor = Color.FromArgb(120, 210, 160);
                _serverLog.AppendText(msg + "\n");
                _serverLog.ScrollToCaret();
            });

            _server.OnSessionsChanged += sessions => SafeInvoke(() =>
            {
                int n = sessions.Count;
                _lblSessions.Text      = $"● Active sessions: {n}";
                _lblSessions.ForeColor = n > 0 ? C_GREEN : C_MUTED;
            });

            _server.Start();
            _btnStart.Enabled = false;
            _btnStop.Enabled  = true;
            _nudPort.Enabled  = _nudMaxConc.Enabled = false;
        }
        catch (Exception ex)
        {
            ShowError($"Failed to start server:\n{ex.Message}");
        }
    }

    private void BtnStop_Click(object? s, EventArgs e)
    {
        _server?.Stop();
        _btnStart.Enabled = true;
        _btnStop.Enabled  = false;
        _nudPort.Enabled  = _nudMaxConc.Enabled = true;
        _lblSessions.Text      = "● Active sessions: 0";
        _lblSessions.ForeColor = C_MUTED;
    }

    // ── Client ────────────────────────────────────────────────────────────────
    private async void BtnCompile_Click(object? s, EventArgs e)
    {
        _compileCts = new CancellationTokenSource();
        SetCompiling(true);
        _output.Clear();

        _client = new CompilerClient(_txtIp.Text.Trim(), (int)_nudClientPort.Value);
        _client.OnLog += msg => SafeInvoke(() =>
        {
            _output.SelectionColor = C_MUTED;
            _output.AppendText(msg + "\n");
        });

        string result = await _client.CompileAsync(_editor.Text, _compileCts.Token);

        SafeInvoke(() =>
        {
            bool isError = result.StartsWith("[ERROR")
                        || result.StartsWith("[COMPILATION FAILED")
                        || result.StartsWith("[CONNECTION")
                        || result.StartsWith("[CANCELLED");

            _output.AppendText("\n");
            _output.SelectionColor = isError ? C_RED : C_GREEN;
            _output.AppendText(result);
            _output.ScrollToCaret();
            SetCompiling(false);
        });
    }

    private void SetCompiling(bool active)
    {
        _btnCompile.Enabled    = !active;
        _btnCancel.Enabled     = active;
        _progress.Visible      = active;
        _lblStatus.Visible     = !active;
        _lblStatus.Text        = "Ready";
    }

    // ── Editor keystroke → throttled highlight ────────────────────────────────
    private void Editor_TextChanged(object? s, EventArgs e)
    {
        _hlTimer.Stop();
        _hlTimer.Start();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  SYNTAX HIGHLIGHTING
    // ═══════════════════════════════════════════════════════════════════════════
    private void ApplyHighlight()
    {
        if (_highlighting) return;
        _highlighting = true;

        int caret = _editor.SelectionStart;
        SendMessage(_editor.Handle, 0x000B, (IntPtr)0, IntPtr.Zero);   // WM_SETREDRAW off

        // Reset all
        _editor.SelectAll();
        _editor.SelectionColor = C_TEXT;

        string text = _editor.Text;

        // Preprocessor directives (#include, #define …)
        HighlightLines(text, l => l.TrimStart().StartsWith("#"), C_PREPROCESSOR);

        // Line comments
        HighlightPattern(text, "//", "\n", C_COMMENT, includeEnd: false);

        // String literals
        HighlightPattern(text, "\"", "\"", C_STRING, includeEnd: true, skipEscape: true);

        // Keywords
        string[] keywords =
        {
            "int","long","short","char","bool","float","double","void","auto","const",
            "static","extern","inline","virtual","override","final","explicit","mutable",
            "volatile","register","signed","unsigned",
            "if","else","while","for","do","switch","case","break","continue","return",
            "class","struct","union","enum","namespace","using","typedef","template",
            "typename","public","private","protected","friend","operator","new","delete",
            "this","nullptr","true","false","try","catch","throw","noexcept",
            "std","cout","cin","cerr","endl","string","vector","map","set","pair",
        };
        foreach (var kw in keywords)
            HighlightWord(text, kw, C_KW);

        _editor.SelectionStart  = caret;
        _editor.SelectionLength = 0;
        SendMessage(_editor.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);   // WM_SETREDRAW on
        _editor.Invalidate();
        _highlighting = false;
    }

    private void HighlightWord(string text, string word, Color color)
    {
        int idx = 0;
        while ((idx = text.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
        {
            bool prevOk = idx == 0          || !char.IsLetterOrDigit(text[idx - 1]) && text[idx - 1] != '_';
            bool nextOk = idx + word.Length >= text.Length || !char.IsLetterOrDigit(text[idx + word.Length]) && text[idx + word.Length] != '_';
            if (prevOk && nextOk)
            {
                _editor.Select(idx, word.Length);
                _editor.SelectionColor = color;
            }
            idx += word.Length;
        }
    }

    private void HighlightLines(string text, Func<string, bool> predicate, Color color)
    {
        int pos = 0;
        foreach (var line in text.Split('\n'))
        {
            if (predicate(line))
            {
                _editor.Select(pos, line.Length);
                _editor.SelectionColor = color;
            }
            pos += line.Length + 1;
        }
    }

    private void HighlightPattern(string text, string start, string end,
        Color color, bool includeEnd, bool skipEscape = false)
    {
        int idx = 0;
        while ((idx = text.IndexOf(start, idx, StringComparison.Ordinal)) >= 0)
        {
            int endIdx = idx + start.Length;
            while (endIdx < text.Length)
            {
                if (skipEscape && text[endIdx] == '\\') { endIdx += 2; continue; }
                int found = text.IndexOf(end, endIdx, StringComparison.Ordinal);
                if (found < 0) { endIdx = text.Length; break; }
                endIdx = found + (includeEnd ? end.Length : 0);
                break;
            }
            int len = endIdx - idx;
            if (len > 0)
            {
                _editor.Select(idx, len);
                _editor.SelectionColor = color;
            }
            idx += Math.Max(1, len);
        }
    }

    // ── Line / Col indicator ─────────────────────────────────────────────────
    private void UpdateLineCol()
    {
        int pos  = _editor.SelectionStart;
        int line = _editor.GetLineFromCharIndex(pos);
        int col  = pos - _editor.GetFirstCharIndexFromLine(line);
        _lblLineCol.Text = $"Ln {line + 1},  Col {col + 1}";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  HELPERS — CONTROL FACTORY
    // ═══════════════════════════════════════════════════════════════════════════
    private static TabPage MakePage(string title) =>
        new TabPage(title) { BackColor = Color.FromArgb(18, 20, 28), ForeColor = Color.FromArgb(215, 218, 230) };

    private Panel MakeHeaderBar(string title)
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = C_HEADER };
        var lbl = new Label
        {
            Text      = title,
            ForeColor = Color.FromArgb(160, 200, 255),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Location  = new Point(10, 10),
        };
        bar.Controls.Add(lbl);
        DrawBorder(bar, bottom: true);
        return bar;
    }

    private Button MakeSmallButton(string text)
    {
        var btn = new Button
        {
            Text      = text,
            Dock      = DockStyle.Right,
            Width     = 72,
            Height    = 36,
            FlatStyle = FlatStyle.Flat,
            BackColor = C_SURFACE,
            ForeColor = C_MUTED,
            Font      = new Font("Segoe UI", 8.5f),
            Cursor    = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static Label MakeLabel(string text, int x, int y) =>
        new Label { Text = text, Location = new Point(x, y), AutoSize = true, ForeColor = Color.FromArgb(160, 163, 185) };

    private Button MakeButton(string text, Color bg, int x, int y, int w, int h)
    {
        var btn = new Button
        {
            Text      = text,
            Location  = new Point(x, y),
            Size      = new Size(w, h),
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor    = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize       = 0;
        btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
        return btn;
    }

    private NumericUpDown MakeNud(int min, int max, int val, int w, int x, int y) =>
        new NumericUpDown
        {
            Minimum     = min, Maximum = max, Value = val,
            Width       = w,   Location = new Point(x, y),
            BackColor   = C_SURFACE,
            ForeColor   = C_TEXT,
            BorderStyle = BorderStyle.FixedSingle,
        };

    private static void DrawBorder(Control ctrl, bool bottom = false, bool top = false)
    {
        ctrl.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(55, 58, 75));
            if (bottom) e.Graphics.DrawLine(pen, 0, ctrl.Height - 1, ctrl.Width, ctrl.Height - 1);
            if (top)    e.Graphics.DrawLine(pen, 0, 0,               ctrl.Width, 0);
        };
    }

    // ── Thread-safe UI invoke ─────────────────────────────────────────────────
    private void SafeInvoke(Action action)
    {
        if (InvokeRequired) Invoke(action);
        else action();
    }

    private void ShowError(string msg) =>
        MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ── Default code snippet ──────────────────────────────────────────────────
    private void LoadDefaultCode()
    {
        _editor.Text =
            "#include <iostream>\n" +
            "#include <string>\n" +
            "#include <vector>\n\n" +
            "int main() {\n" +
            "    std::vector<std::string> messages = {\n" +
            "        \"Hello from Remote Compiler!\",\n" +
            "        \"Threading works great.\",\n" +
            "        \"Multiple clients supported.\"\n" +
            "    };\n\n" +
            "    for (const auto& msg : messages) {\n" +
            "        std::cout << msg << std::endl;\n" +
            "    }\n\n" +
            "    int result = 0;\n" +
            "    for (int i = 1; i <= 10; i++) result += i;\n" +
            "    std::cout << \"Sum 1-10 = \" << result << std::endl;\n\n" +
            "    return 0;\n" +
            "}\n";
    }

    // ── Cleanup on close ──────────────────────────────────────────────────────
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _server?.Stop();
        _compileCts?.Cancel();
        _hlTimer.Dispose();
        base.OnFormClosing(e);
    }
    // ── P/Invoke for flicker-free highlight ───────────────────────────────────
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

}
