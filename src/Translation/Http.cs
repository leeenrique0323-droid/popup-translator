using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace PopupTranslator.Translation;

internal static class Http
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(8),
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return c;
    }

    /// <summary>把網路例外轉成中文訊息的 TranslationException。</summary>
    public static async Task<string> SendAsync(Func<HttpRequestMessage> make, string serviceName,
        Func<HttpStatusCode, string?>? statusMessage, CancellationToken ct)
    {
        HttpResponseMessage resp;
        using var req = make();
        try
        {
            resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new TranslationException($"連線逾時：{serviceName} 沒有回應，請稍後再試。", ex);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException)
        {
            throw new TranslationException($"網路連線失敗：無法連到 {serviceName}，請檢查網路或防火牆／Proxy 設定。", ex);
        }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException)
        {
            throw new TranslationException("安全連線（HTTPS）失敗，請確認系統時間正確或網路未被攔截。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException($"網路錯誤：無法連到 {serviceName}（{ex.Message}）", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                var custom = statusMessage?.Invoke(resp.StatusCode);
                if (custom != null) throw new TranslationException(custom);
                int code = (int)resp.StatusCode;
                if (code == 429)
                    throw new TranslationException($"{serviceName} 請求過於頻繁，已被暫時限制，請稍候幾分鐘再試。");
                if (code >= 500)
                    throw new TranslationException($"{serviceName} 伺服器暫時無法使用（HTTP {code}），請稍後再試。");
                throw new TranslationException($"{serviceName} 回應錯誤（HTTP {code}）。");
            }
            try
            {
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new TranslationException($"讀取 {serviceName} 回應時網路中斷，請再試一次。", ex);
            }
        }
    }
}
