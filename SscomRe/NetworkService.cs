using System.Net;
using System.Net.Sockets;

namespace SscomRe;

/// <summary>网络模式（对应原程序 0x4ED534 的取值）。</summary>
public enum NetMode
{
    Serial = 0,
    TcpClient = 1,
    TcpServer = 2,
    Udp = 3,
}

/// <summary>
/// 网络通道（规格「网络：TCP 和 UDP」）。
/// 就绪标志（原程序 0x4ED535）语义：TCP 客户端连上、服务端接到连接、UDP 套接字建好时置 1；
/// 断开、出错、切换模式时清 0；网络模式下发数据要求就绪。
/// 事件在后台线程触发，界面侧自行调度。
/// </summary>
public sealed class NetworkService : IDisposable
{
    /// <summary>第一个连接之外再保留的连接数。再多的直接关掉，避免被不断连入耗尽句柄。</summary>
    private const int MaxExtraClients = 8;

    private readonly object _gate = new();
    private int _generation;
    private TcpClient? _client;
    private TcpListener? _listener;
    private TcpClient? _serverFirst;                 // 规格：服务端发送只发第 0 个连接
    private readonly List<TcpClient> _serverOthers = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public bool Ready { get; private set; }

    /// <summary>服务端/UDP 实际绑定的本地端口（传 0 时由系统分配；测试与显示用）。</summary>
    public int BoundPort
    {
        get
        {
            lock (_gate)
            {
                if (_listener?.LocalEndpoint is IPEndPoint le) return le.Port;
                if (_udp?.Client.LocalEndPoint is IPEndPoint ue) return ue.Port;
                return 0;
            }
        }
    }

    public event Action? ReadyChanged;
    public event Action<byte[]>? DataReceived;
    public event Action<string>? RemoteIpLearned;    // UDP 收到数据时回报发送方 IP（写回“远程IP”）
    public event Action<string>? ErrorRaised;

    private void SetReady(bool value)
    {
        if (Ready == value) return;
        Ready = value;
        ReadyChanged?.Invoke();
    }

    private void Fail(string message)
    {
        SetReady(false);
        ErrorRaised?.Invoke(message);
    }

    private bool IsCurrent(int generation)
    {
        lock (_gate) return generation == _generation;
    }

    /// <summary>TCP 客户端：填好主机/端口后 SetActive + connect（规格 ConnectClick 模式 1）。</summary>
    public void ConnectClient(string host, int port)
    {
        Disconnect();
        int generation;
        CancellationToken token;
        TcpClient client;
        lock (_gate)
        {
            generation = ++_generation;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            client = new TcpClient { NoDelay = true };
            _client = client;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var connect = client.ConnectAsync(host, port);
                var delay = Task.Delay(TimeSpan.FromSeconds(5), token);
                if (await Task.WhenAny(connect, delay) != connect)
                {
                    try { client.Close(); } catch { /* 超时或取消时打断尚未完成的连接 */ }
                    if (token.IsCancellationRequested) return;
                    throw new TimeoutException("连接超时");
                }
                await connect;
                if (!IsCurrent(generation)) return;
                SetReady(true);                       // 连接成功 → 就绪
                _ = ReadLoopAsync(client, token, () =>
                {
                    if (IsCurrent(generation)) SetReady(false);
                });
            }
            catch (Exception ex)
            {
                // 断开或被新一次连接替换时，不要清掉新连接的就绪，也不要弹失败框。
                if (!IsCurrent(generation) || token.IsCancellationRequested) return;
                Fail($"TCP客户端连接失败：{ex.Message}");
            }
        });
    }

    /// <summary>TCP 服务端：listen（backlog 5），接受第一个连接时置就绪（规格模式 2）。</summary>
    public void StartServer(int port)
    {
        Disconnect();
        int generation;
        CancellationToken token;
        TcpListener listener;
        lock (_gate)
        {
            generation = ++_generation;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start(5);
            _listener = listener;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && IsCurrent(generation))
                {
                    var conn = await listener.AcceptTcpClientAsync();
                    conn.NoDelay = true;
                    bool first;
                    bool reject;
                    lock (_gate)
                    {
                        if (generation != _generation)
                        {
                            reject = true;
                            first = false;
                        }
                        else if (_serverFirst is null)
                        {
                            _serverFirst = conn;
                            first = true;
                            reject = false;
                        }
                        else if (_serverOthers.Count >= MaxExtraClients)
                        {
                            first = false;
                            reject = true;
                        }
                        else
                        {
                            _serverOthers.Add(conn);
                            first = false;
                            reject = false;
                        }
                    }
                    if (reject)
                    {
                        SafeCloseClient(conn);
                        continue;
                    }
                    if (first)
                    {
                        SetReady(true);               // 接到一个连接 → 就绪
                        _ = ReadLoopAsync(conn, token, () =>
                        {
                            lock (_gate)
                            {
                                if (!ReferenceEquals(_serverFirst, conn)) return;
                                _serverFirst = null;
                            }
                            if (IsCurrent(generation)) SetReady(false);
                        });
                    }
                    else
                    {
                        // 其余连接不参与发送，但必须读走数据，否则对端堵住、本端句柄也不释放。
                        _ = ReadLoopAsync(conn, token, () =>
                        {
                            lock (_gate) _serverOthers.Remove(conn);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrent(generation) || token.IsCancellationRequested) return;
                Fail($"TCP服务端错误：{ex.Message}");
            }
        });
    }

    /// <summary>UDP：绑 0.0.0.0 + 本地端口，建好即置就绪（规格 ConnectClick 模式 3 尾部）。</summary>
    public void StartUdp(int localPort)
    {
        Disconnect();
        int generation;
        CancellationToken token;
        UdpClient udp;
        lock (_gate)
        {
            generation = ++_generation;
            _cts = new CancellationTokenSource();
            token = _cts.Token;
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, localPort));
            _udp = udp;
        }
        SetReady(true);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested && IsCurrent(generation))
                {
                    var result = await udp.ReceiveAsync();
                    if (!IsCurrent(generation)) return;
                    RemoteIpLearned?.Invoke(result.RemoteEndPoint.Address.ToString());
                    DataReceived?.Invoke(result.Buffer);
                }
            }
            catch (Exception ex)
            {
                if (!IsCurrent(generation) || token.IsCancellationRequested) return;
                Fail($"UDP错误：{ex.Message}");
            }
        });
    }

    private async Task ReadLoopAsync(TcpClient client, CancellationToken token, Action onClosed)
    {
        var buffer = new byte[4096];
        try
        {
            var stream = client.GetStream();
            while (!token.IsCancellationRequested)
            {
                int n = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                if (n <= 0) break;
                var chunk = new byte[n];
                Buffer.BlockCopy(buffer, 0, chunk, 0, n);
                DataReceived?.Invoke(chunk);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) ErrorRaised?.Invoke($"连接中断：{ex.Message}");
        }
        finally
        {
            onClosed();
        }
    }

    /// <summary>按模式发送（规格发送分派：TCP 客户端 / 服务端第 0 个连接 / UDP sendto）。</summary>
    public void Send(byte[] data, NetMode mode, string remoteHost, int remotePort)
    {
        IPAddress? udpAddress = null;
        if (mode == NetMode.Udp)
        {
            udpAddress = IPAddress.TryParse(remoteHost, out var parsed)
                ? parsed
                : (Dns.GetHostAddresses(remoteHost).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                   ?? throw new InvalidOperationException("远程IP无法解析"));
        }

        lock (_gate)
        {
            switch (mode)
            {
                case NetMode.TcpClient:
                    if (_client is { Connected: true })
                        _client.GetStream().Write(data, 0, data.Length);
                    break;
                case NetMode.TcpServer:
                    if (_serverFirst is { Connected: true })
                        _serverFirst.GetStream().Write(data, 0, data.Length);
                    break;
                case NetMode.Udp:
                    if (_udp is not null && udpAddress is not null)
                        _udp.Send(data, data.Length, new IPEndPoint(udpAddress, remotePort));
                    break;
            }
        }
    }

    /// <summary>断开一切并清就绪（规格 disConnectClick + 模式切换）。</summary>
    public void Disconnect()
    {
        TcpClient? client;
        TcpListener? listener;
        TcpClient? first;
        TcpClient[] others;
        UdpClient? udp;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _generation++;
            cts = _cts;
            _cts = null;
            client = _client;
            _client = null;
            listener = _listener;
            _listener = null;
            first = _serverFirst;
            _serverFirst = null;
            others = _serverOthers.ToArray();
            _serverOthers.Clear();
            udp = _udp;
            _udp = null;
        }

        try { cts?.Cancel(); } catch { /* ignore */ }
        cts?.Dispose();
        SafeCloseClient(client);
        try { listener?.Stop(); } catch { /* ignore */ }
        SafeCloseClient(first);
        foreach (var c in others) SafeCloseClient(c);
        try { udp?.Dispose(); } catch { /* ignore */ }
        SetReady(false);
    }

    private static void SafeCloseClient(TcpClient? c)
    {
        if (c is null) return;
        try { c.Close(); } catch { /* ignore */ }
    }

    public void Dispose() => Disconnect();
}
