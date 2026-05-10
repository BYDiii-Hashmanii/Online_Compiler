using System.Net.Sockets;
using System.Text;

namespace RemoteCompiler;

// ── Async TCP client — sends C++ code, returns server output ─────────────────
public class CompilerClient
{
    private readonly string _ip;
    private readonly int    _port;
    private const    int    CONNECT_TIMEOUT_MS = 5_000;
    private const    int    READ_TIMEOUT_MS    = 35_000;
    private const    int    BUFFER_SIZE        = 64 * 1024;

    public event Action<string>? OnLog;

    public CompilerClient(string ip = "127.0.0.1", int port = 8888)
    {
        _ip   = ip;
        _port = port;
    }

    // ── Main API call ─────────────────────────────────────────────────────────
    public async Task<string> CompileAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "[ERROR] Code is empty.";

        try
        {
            Log($"Connecting to {_ip}:{_port} …");

            using var tcp = new TcpClient();

            // Connect with timeout
            var connect = tcp.ConnectAsync(_ip, _port, ct).AsTask();
            if (await Task.WhenAny(connect, Task.Delay(CONNECT_TIMEOUT_MS, ct)) != connect)
                return $"[ERROR] Connection timed out after {CONNECT_TIMEOUT_MS / 1000}s.";
            await connect;   // rethrow socket error if any

            Log("Connected. Sending code …");

            using var stream = tcp.GetStream();
            stream.ReadTimeout = READ_TIMEOUT_MS;

            // Send
            var codeBytes = Encoding.UTF8.GetBytes(code);
            await stream.WriteAsync(codeBytes, ct);
            Log($"Sent {codeBytes.Length} bytes. Waiting for result …");

            // Receive (may arrive in multiple chunks)
            var sb  = new StringBuilder();
            var buf = new byte[BUFFER_SIZE];
            int n;
            while ((n = await stream.ReadAsync(buf, ct)) > 0)
            {
                sb.Append(Encoding.UTF8.GetString(buf, 0, n));
                if (!stream.DataAvailable) break;
            }

            string result = sb.ToString();
            Log($"Result received ({result.Length} chars).");
            return result;
        }
        catch (SocketException ex)
        {
            return $"[CONNECTION ERROR] Cannot reach {_ip}:{_port}\n{ex.Message}";
        }
        catch (OperationCanceledException)
        {
            return "[CANCELLED] Request was cancelled.";
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.Message}";
        }
    }

    private void Log(string msg) => OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}]  {msg}");
}
