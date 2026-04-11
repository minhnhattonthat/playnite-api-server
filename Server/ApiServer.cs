using System;
using System.Net;
using System.Threading;
using Playnite.SDK;

namespace PlayniteApiServer.Server
{
    /// <summary>
    /// Owns an HttpListener lifecycle + a dedicated accept thread.
    /// Requests are dispatched to the thread pool so shutdown can bound its wait.
    /// Safe to Stop() from OnApplicationStopped; stop is capped to ~2 s.
    /// </summary>
    internal sealed class ApiServer : IDisposable
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly Router router;
        private readonly int port;
        private readonly string bindAddress;

        private HttpListener listener;
        private Thread acceptThread;
        private CountdownEvent inflight;
        private volatile bool running;
        private readonly object gate = new object();

        public int Port => port;

        public ApiServer(Router router, int port, string bindAddress)
        {
            this.router = router;
            this.port = port;
            this.bindAddress = bindAddress ?? "127.0.0.1";
        }

        public void Start()
        {
            lock (gate)
            {
                if (running)
                {
                    return;
                }

                listener = new HttpListener();
                var prefix = "http://" + bindAddress + ":" + port + "/";
                listener.Prefixes.Add(prefix);

                try
                {
                    listener.Start();
                }
                catch (HttpListenerException ex)
                {
                    // 5   = ERROR_ACCESS_DENIED (URL ACL missing — unusual for 127.0.0.1 but possible)
                    // 183 = ERROR_ALREADY_EXISTS (port in use)
                    logger.Error(ex, "HttpListener.Start failed for prefix " + prefix + " (code=" + ex.ErrorCode + ")");
                    listener = null;
                    throw;
                }

                inflight = new CountdownEvent(1); // seed
                running = true;
                acceptThread = new Thread(AcceptLoop)
                {
                    IsBackground = true,
                    Name = "PlayniteApiServer.Accept",
                };
                acceptThread.Start();
                logger.Info("PlayniteApiServer listening on " + prefix);
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                if (!running)
                {
                    return;
                }

                running = false;
            }

            try { listener?.Stop(); } catch { /* ignore */ }

            // Release the seed so the countdown can reach zero once inflight requests drain.
            try { inflight?.Signal(); } catch { /* already signalled */ }

            // Bounded wait — don't delay Playnite shutdown.
            try { inflight?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }

            try { listener?.Close(); } catch { /* ignore */ }
            try { acceptThread?.Join(500); } catch { /* ignore */ }

            listener = null;
            acceptThread = null;
            inflight?.Dispose();
            inflight = null;

            logger.Info("PlayniteApiServer stopped.");
        }

        private void AcceptLoop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (!running)
                {
                    try { ctx.Response.Close(); } catch { /* ignore */ }
                    break;
                }

                try
                {
                    inflight.AddCount();
                }
                catch (InvalidOperationException)
                {
                    // Countdown already at zero — shutting down.
                    try { ctx.Response.Close(); } catch { /* ignore */ }
                    break;
                }

                ThreadPool.QueueUserWorkItem(state =>
                {
                    var c = (HttpListenerContext)state;
                    try
                    {
                        router.Dispatch(c);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Unhandled dispatch exception.");
                    }
                    finally
                    {
                        try { c.Response.Close(); } catch { /* ignore */ }
                        try { inflight.Signal(); } catch { /* already at zero */ }
                    }
                }, ctx);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
