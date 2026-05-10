using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace RemoteCompiler;

// ── Tracks one connected client ──────────────────────────────────────────────
public class ClientSession
{
    public Guid   Id            { get; }      = Guid.NewGuid();
    public string EndPoint      { get; set; } = "Unknown";
    public string Status        { get; set; } = "Connected";
    public DateTime ConnectedAt { get; }      = DateTime.Now;
}

// ── The server engine (no UI dependency) ─────────────────────────────────────
public class CompilerServerEngine
{
    // ── Config ───────────────────────────────────────────────────────────────
    private readonly int  _port;
    private readonly int  _maxConcurrent;
    private const    int  SESSION_TIMEOUT_SEC = 30;
    private const    int  EXEC_TIMEOUT_SEC    = 10;
    private const    int  BUFFER_SIZE         = 64 * 1024;   // 64 KB

    // ── State ────────────────────────────────────────────────────────────────
    private TcpListener?                              _listener;
    private CancellationTokenSource?                  _cts;
    private readonly SemaphoreSlim                    _semaphore;
    private readonly ConcurrentDictionary<Guid, ClientSession> _sessions = new();

    // ── Events (subscribed by the UI) ────────────────────────────────────────
    public event Action<string>?                                          OnLog;
    public event Action<ConcurrentDictionary<Guid, ClientSession>>?       OnSessionsChanged;

    public bool IsRunning { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────
    public CompilerServerEngine(int port = 8888, int maxConcurrent = 4)
    {
        _port           = port;
        _maxConcurrent  = maxConcurrent;
        _semaphore      = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    // ── Start / Stop ─────────────────────────────────────────────────────────
    public void Start()
    {
        _cts      = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        IsRunning = true;
        Log($"Server online  |  port {_port}  |  max concurrent: {_maxConcurrent}");
        Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;
        Log("Server stopped.");
    }

    // ── Accept loop (runs on background thread) ───────────────────────────────
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcp = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(tcp, ct), ct);
            }
            catch when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Log($"[Accept error] {ex.Message}"); }
        }
    }

    // ── Per-client handler ────────────────────────────────────────────────────
    private async Task HandleClientAsync(TcpClient tcp, CancellationToken parentCt)
    {
        var session = new ClientSession
        {
            EndPoint = tcp.Client.RemoteEndPoint?.ToString() ?? "?"
        };
        _sessions[session.Id] = session;
        OnSessionsChanged?.Invoke(_sessions);
        Log($"[{session.EndPoint}] connected  |  active: {_sessions.Count}");

        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        sessionCts.CancelAfter(TimeSpan.FromSeconds(SESSION_TIMEOUT_SEC));
        var ct = sessionCts.Token;

        try
        {
            using (tcp)
            using (var stream = tcp.GetStream())
            {
                // ── Read incoming C++ code ──────────────────────────────────
                var buf  = new byte[BUFFER_SIZE];
                int read = await stream.ReadAsync(buf, ct);
                if (read == 0) return;

                string code = Encoding.UTF8.GetString(buf, 0, read);
                Log($"[{session.EndPoint}] received {read} bytes  →  queued");

                session.Status = "Compiling";
                OnSessionsChanged?.Invoke(_sessions);

                // ── Throttled compile & run ────────────────────────────────
                await _semaphore.WaitAsync(ct);
                string result;
                try   { result = await CompileAndRunAsync(code, session.EndPoint, ct); }
                finally { _semaphore.Release(); }

                // ── Send result back ───────────────────────────────────────
                var bytes = Encoding.UTF8.GetBytes(result);
                await stream.WriteAsync(bytes, ct);
                Log($"[{session.EndPoint}] response sent  ({bytes.Length} bytes)");
            }
        }
        catch (OperationCanceledException)
        {
            Log($"[{session.EndPoint}] session timed out ({SESSION_TIMEOUT_SEC}s)");
        }
        catch (Exception ex)
        {
            Log($"[{session.EndPoint}] error: {ex.Message}");
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            OnSessionsChanged?.Invoke(_sessions);
            Log($"[{session.EndPoint}] disconnected  |  active: {_sessions.Count}");
        }
    }

    // ── Compile + execute in temp files ──────────────────────────────────────
    private async Task<string> CompileAndRunAsync(string code, string clientId, CancellationToken ct)
    {
        string uid  = Guid.NewGuid().ToString("N")[..8];
        string src  = Path.Combine(Path.GetTempPath(), $"rc_{uid}.cpp");
        string exe  = Path.Combine(Path.GetTempPath(), $"rc_{uid}.exe");

        try
        {
            await File.WriteAllTextAsync(src, code, ct);

            // ── g++ compile ────────────────────────────────────────────────
            using var compiler = StartProcess("g++", $"\"{src}\" -o \"{exe}\" -std=c++17");
            string   compileErr = await compiler.StandardError.ReadToEndAsync(ct);
            await    compiler.WaitForExitAsync(ct);

            if (compiler.ExitCode != 0)
            {
                Log($"[{clientId}] compile FAILED");
                return $"[COMPILATION FAILED]\n{compileErr}";
            }

            Log($"[{clientId}] compiled OK  →  running...");

            // ── execute with hard timeout ──────────────────────────────────
            using var execCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            execCts.CancelAfter(TimeSpan.FromSeconds(EXEC_TIMEOUT_SEC));

            using var executor = StartProcess(exe, "");
            string output     = await executor.StandardOutput.ReadToEndAsync(execCts.Token);
            string runtimeErr = await executor.StandardError.ReadToEndAsync(execCts.Token);
            await  executor.WaitForExitAsync(execCts.Token);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[EXECUTION OUTPUT]");
            sb.AppendLine(string.IsNullOrWhiteSpace(output) ? "(no output)" : output.TrimEnd());
            if (!string.IsNullOrWhiteSpace(runtimeErr))
                sb.AppendLine($"\n[RUNTIME ERRORS]\n{runtimeErr.TrimEnd()}");
            sb.AppendLine($"\n[Exit code: {executor.ExitCode}]");
            return sb.ToString();
        }
        catch (OperationCanceledException)
        {
            return $"[ERROR] Execution exceeded {EXEC_TIMEOUT_SEC}s limit — possible infinite loop.";
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(exe); } catch { }
        }
    }

    // ── Helper: create a hidden process ──────────────────────────────────────
    private static Process StartProcess(string file, string args)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = file,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };
        p.Start();
        return p;
    }

    private void Log(string msg) =>
        OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}]  {msg}");
}
