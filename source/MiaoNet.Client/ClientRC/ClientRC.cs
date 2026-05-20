using System.Net;
using System.Text;

namespace Celeste.Mod.MiaoNet;

public static class ClientRC
{
    public static string? AuthenticationCode
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    private static CancellationTokenSource? cts;

    public static void Start()
    {
        if (cts is null || cts.IsCancellationRequested)
        {
            Logger.Info(LT.MiaoNetRC, "Starting client RC...");
            const int SecondsTimeout = 120;
            cts = new CancellationTokenSource(SecondsTimeout * 1000);
            Thread th = new(new ParameterizedThreadStart(RCThread));
            th.Start(cts.Token);
        }
        else
        {
            Logger.Info(LT.MiaoNetRC, "Client RC is already running.");
        }
    }

    public static void Stop()
    {
        if (cts is not null && !cts.IsCancellationRequested)
        {
            Logger.Info(LT.MiaoNetRC, "Stopping client RC...");
            cts.Cancel();
            cts = null;
        }
        else
        {
            Logger.Info(LT.MiaoNetRC, "Client RC is not running, no need to stop.");
        }
    }

    private static void RCThread(object? tokenObj)
    {
        CancellationToken token = (CancellationToken)tokenObj!;

        Logger.Info(LT.MiaoNetRC, "Client RC is running.");

        try
        {
            HttpListener listener = new();
            listener.Prefixes.Add("http://localhost:21472/");
            token.Register(state =>
            {
                var l = (HttpListener)state!;
                if (l.IsListening)
                    l.Close();
            }, listener);

            listener.Start();
            while (!token.IsCancellationRequested)
            {
                var ctx = listener.GetContext();

                Logger.Info(LT.MiaoNetRC, $"Requested URL: {ctx.Request.Url!.AbsolutePath}");
                HandleRequest(ctx);
                ctx.Response.Close();
            }
        }
        catch (OperationCanceledException)
        { }
        catch (HttpListenerException e)
        when (e.ErrorCode == 995)
        { }
        catch (Exception e)
        {
            Logger.Error(LT.MiaoNetRC, "Unhandled exception!");
            Logger.LogDetailed(e, LT.MiaoNetRC);
        }

        Logger.Info(LT.MiaoNetRC, "Client RC stopped.");
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;

        // idk why req.Url is nullable
        string endPoint = req.Url!.AbsolutePath;

        if (endPoint == "/auth")
        {
            string? code = req.QueryString["code"];
            if (code is null)
            {
                res.StatusCode = (int)HttpStatusCode.BadRequest;
                res.OutputStream.Write(Encoding.UTF8.GetBytes("No code provided."));
            }
            else
            {
                AuthenticationCode = code;
                res.Redirect($"/success?lang={GameLanguage.GetRCLang()}");
            }
        }
        else if (endPoint == "/success")
        {
            const string FileName = "ClientRC.success.html";
            using var resStream = typeof(ClientRC).Assembly.GetManifestResourceStream(FileName)
                ?? throw new FileNotFoundException(null, FileName);
            resStream.CopyTo(res.OutputStream);
            res.ContentType = "text/html; charset=utf-8";
            res.StatusCode = (int)HttpStatusCode.OK;
        }
        else if (endPoint == "/raise-game")
        {
            nint handle = Celeste.Instance.Window.Handle;
            SDL2.SDL.SDL_RaiseWindow(handle);
            MiaoNetModule.Instance.MiaoNetContext.QueueConnect();
            res.StatusCode = (int)HttpStatusCode.NoContent;
        }
        else
        {
            res.StatusCode = (int)HttpStatusCode.NotFound;
        }
    }
}
