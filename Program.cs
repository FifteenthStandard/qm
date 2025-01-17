using System.CommandLine;
using System.Net;
using System.Net.Sockets;

var builder = WebApplication.CreateBuilder(args);
var command = new Command(builder);
return await command.InvokeAsync(args);

class Command : RootCommand
{
    public Command(WebApplicationBuilder builder) : base("Quartermaster")
    {
        Name = "qm";
        var root = new Option<string>("--root", () => Directory.GetCurrentDirectory(), "Directory root");
        var port = new Option<int>("--port", () => 8080, "Port");

        Add(root); Add(port);

        this.SetHandler((root, port) =>
        {
            if (string.IsNullOrWhiteSpace(root)) root = Environment.CurrentDirectory;
            root = Path.IsPathFullyQualified(root)
                ? root
                : Path.Join(Environment.CurrentDirectory, root);

            builder.WebHost.UseKestrel(options =>
            {
                options.ListenAnyIP(port);
            });

            var app = builder.Build();

            var service = new TorrentService(root);
            service.AutoDetect();

            app.MapGet("/api/torrents", () =>
                service.Torrents);
            app.MapGet("/api/search/{searchString}", async (string searchString) =>
                await service.SearchAsync(searchString));
            app.MapGet("/api/torrent/{infoHash}", (string infoHash) =>
                service.GetTorrent(infoHash));

            app.UseDefaultFiles();
            app.UseStaticFiles();

            Console.WriteLine($"Serving content from {root}");
            Console.WriteLine($"Local IP Addresses are {string.Join(", ", LocalIps(port))}");

            app.Run();
        }, root, port);
    }

    private IEnumerable<string> LocalIps(int port)
        => Dns.GetHostEntry(Dns.GetHostName())
            .AddressList
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            .Select(ip => $"http://{ip}:{port}");
}