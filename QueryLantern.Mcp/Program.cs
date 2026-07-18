namespace QueryLantern.Mcp;

using System.IO;

/// <summary>
/// Entry point for the QueryLantern MCP server. Reads newline-delimited JSON-RPC requests from stdin
/// and writes responses to stdout. Configure the catalog path with the CATALOG_PATH environment
/// variable; set QL_MCP_TOKEN to require an authToken on every request.
/// </summary>
public static class Program
{
    public static async Task Main(string[] args)
    {
        var catalogPath = Environment.GetEnvironmentVariable("CATALOG_PATH") ?? "catalog.db";
        var server = new McpServer(catalogPath);

        if (Console.IsInputRedirected)
        {
            string? line;
            while ((line = await Console.In.ReadLineAsync()) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var response = await server.HandleAsync(line);
                await Console.Out.WriteLineAsync(response);
                await Console.Out.FlushAsync();
            }
        }
        else
        {
            Console.WriteLine("QueryLantern MCP server. Pipe JSON-RPC requests on stdin (one per line).");
            await Task.Delay(-1);
        }
    }
}
