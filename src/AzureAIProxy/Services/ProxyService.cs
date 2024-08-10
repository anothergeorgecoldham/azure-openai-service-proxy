using System.Net;
using System.Text;
using System.Text.Json;
using AzureAIProxy.Shared.Database;

namespace AzureAIProxy.Services;

public class ProxyService(IHttpClientFactory httpClientFactory, IMetricService metricService)
    : IProxyService
{
    private const int HttpTimeoutSeconds = 60;

    public async Task<(string responseContent, int statusCode)> HttpDeleteAsync(
        UriBuilder requestUrl,
        string endpointKey,
        HttpContext context,
        RequestContext requestContext,
        Deployment deployment
    )
    {
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

        var requestUrlWithQuery = AppendQueryParameters(requestUrl, context);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Delete, requestUrlWithQuery);
        requestMessage.Headers.Add("api-key", endpointKey);

        var response = await httpClient.SendAsync(requestMessage);
        var responseContent = await response.Content.ReadAsStringAsync();

        return (responseContent, (int)response.StatusCode);
    }

    public async Task<(string responseContent, int statusCode)> HttpGetAsync(
        UriBuilder requestUrl,
        string endpointKey,
        HttpContext context,
        RequestContext requestContext,
        Deployment deployment
    )
    {
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

        var requestUrlWithQuery = AppendQueryParameters(requestUrl, context);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Get, requestUrlWithQuery);
        requestMessage.Headers.Add("api-key", endpointKey);

        var response = await httpClient.SendAsync(requestMessage);

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        switch (mediaType)
        {
            case string type when type.Contains("application/json"):
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return (responseContent, (int)response.StatusCode);
                }

            case string type when type.Contains("application/octet-stream"):
                {
                    context.Response.ContentType = "application/octet-stream";
                    await using var responseStream = await response.Content.ReadAsStreamAsync();
                    await responseStream.CopyToAsync(context.Response.Body);
                    return (string.Empty, (int)response.StatusCode);
                }

            default:
                return (string.Empty, (int)HttpStatusCode.UnsupportedMediaType);
        }
    }

    /// <summary>
    /// Sends an HTTP POST request with the specified JSON object to the specified request URL using the provided endpoint key.
    /// </summary>
    /// <param name="requestJson">The JSON object to send in the request body.</param>
    /// <param name="requestUrl">The URL of the request.</param>
    /// <param name="endpointKey">The endpoint key to use for authentication.</param>
    /// <returns>A tuple containing the response content and the status code of the HTTP response.</returns>
    public async Task<(string responseContent, int statusCode)> HttpPostAsync(
        UriBuilder requestUrl,
        string endpointKey,
        HttpContext context,
        JsonDocument requestJsonDoc,
        RequestContext requestContext,
        Deployment deployment
    )
    {
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

        var requestUrlWithQuery = AppendQueryParameters(requestUrl, context);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrlWithQuery);
        requestMessage.Content = new StringContent(
            requestJsonDoc.RootElement.ToString(),
            Encoding.UTF8,
            "application/json"
        );
        requestMessage.Headers.Add("api-key", endpointKey);

        var response = await httpClient.SendAsync(requestMessage);
        var responseContent = await response.Content.ReadAsStringAsync();
        await metricService.LogApiUsageAsync(requestContext, deployment, responseContent);

        return (responseContent, (int)response.StatusCode);
    }

    /// <summary>
    /// Sends an HTTP POST request with a stream body asynchronously.
    /// </summary>
    /// <param name="requestUrl">The URL of the request.</param>
    /// <param name="endpointKey">The API key for the endpoint.</param>
    /// <param name="context">The HTTP context.</param>
    /// <param name="requestString">The request body as a string.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HttpPostStreamAsync(
        UriBuilder requestUrl,
        string endpointKey,
        HttpContext context,
        JsonDocument requestJsonDoc,
        RequestContext requestContext,
        Deployment deployment
    )
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);

        var requestUrlWithQuery = AppendQueryParameters(requestUrl, context);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrlWithQuery);
        requestMessage.Content = new StringContent(
            requestJsonDoc.RootElement.ToString(),
            Encoding.UTF8,
            "application/json"
        );
        requestMessage.Headers.Add("api-key", endpointKey);

        var response = await httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead
        );
        await metricService.LogApiUsageAsync(requestContext, deployment, null);

        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = "application/json";

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        await responseStream.CopyToAsync(context.Response.Body);
    }

    /// <summary>
    /// Appends query parameters from the specified <see cref="HttpContext"/> to the given request URL.
    /// </summary>
    /// <param name="requestUrl">The request URL to append the query parameters to.</param>
    /// <param name="context">The <see cref="HttpContext"/> containing the query parameters.</param>
    /// <returns>A new <see cref="Uri"/> object with the appended query parameters.</returns>
    private static Uri AppendQueryParameters(UriBuilder requestUrl, HttpContext context)
    {
        var queryParameters = context.Request.Query
            .Where(q => !string.IsNullOrEmpty(q.Value)) // Skip parameters with empty values
            .Select(q => $"{q.Key}={q.Value!}");

        requestUrl.Query = string.Join("&", queryParameters);
        return requestUrl.Uri;
    }
}
