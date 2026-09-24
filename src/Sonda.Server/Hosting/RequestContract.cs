using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Sonda.Access;

namespace Sonda.Server.Hosting;

/// <summary>Fail closed on unknown filters and enforce limits even for chunked bodies.</summary>
public static class RequestContract
{
    public static async Task Validate(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null && context.Request.Path.StartsWithSegments("/api/v1"))
        {
            var descriptions = context.RequestServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>()
                .ApiDescriptionGroups.Items.SelectMany(x => x.Items);
            var route = (endpoint as RouteEndpoint)?.RoutePattern.RawText?.TrimStart('/');
            var description = descriptions.FirstOrDefault(x => x.HttpMethod == context.Request.Method && x.RelativePath == route);
            var allowed = description?.ParameterDescriptions.Where(x => x.Source.Id == "Query").Select(x => x.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            if (context.Request.Query.Any(x => !allowed.Contains(x.Key) || x.Value.Count != 1))
                throw new AccessFault(400, "unsupported_or_repeated_query_parameter");
        }
        if (context.Request.Method is not ("GET" or "HEAD" or "OPTIONS"))
        {
            var limit = context.Request.Path.Value!.EndsWith("/simulations", StringComparison.Ordinal) ? 1048576 : 262144;
            if (context.Request.ContentLength > limit) throw new AccessFault(413, "request_too_large");
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature) feature.MaxRequestBodySize = limit;
            // A bounded in-memory copy covers TestServer and streams without Content-Length too.
            using var body = new MemoryStream();
            var buffer = new byte[16384]; int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) != 0)
            {
                if (body.Length + read > limit) throw new AccessFault(413, "request_too_large");
                await body.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
            }
            body.Position = 0; var original = context.Request.Body; context.Request.Body = body;
            try { await next(context); } finally { context.Request.Body = original; }
            return;
        }
        await next(context);
    }
}
