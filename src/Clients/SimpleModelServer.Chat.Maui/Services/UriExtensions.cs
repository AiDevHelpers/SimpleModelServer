namespace SimpleModelServer.Chat.Maui.Services;

internal static class UriExtensions
{
    public static Uri AppendPath(this Uri baseUri, string path)
    {
        var uriBuilder = new UriBuilder(baseUri);
        uriBuilder.Path = (uriBuilder.Path.TrimEnd('/') + "/" + path.TrimStart('/')).Replace("//", "/");
        return uriBuilder.Uri;
    }
}
