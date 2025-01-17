public class TorrentService
{
    private readonly string _root;

    public TorrentService(string root)
    {
        _root = root;
    }

    private readonly MonoTorrent.Client.ClientEngine engine = new MonoTorrent.Client.ClientEngine();
    private readonly Dictionary<string, Torrent> torrents = new Dictionary<string, Torrent>();

    public void AutoDetect()
    {
        var filenames = Directory.GetFiles(_root, "*.torrent");
        foreach (var filename in filenames)
        {
            var infoHash = Path.GetFileNameWithoutExtension(filename);
            GetTorrent(infoHash);
        }
    }

    public async Task<IEnumerable<TorrentSearchResult>> SearchAsync(string searchString)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        var url = $"https://apibay.org/q.php?q={System.Web.HttpUtility.UrlEncode(searchString)}";
        return await client.GetFromJsonAsync<TorrentSearchResult[]>(url)
            ?? Enumerable.Empty<TorrentSearchResult>();
    }

    public Torrent GetTorrent(string infoHash)
    {
        if (torrents.TryGetValue(infoHash, out var torrent)) return torrent;
        torrent = new Torrent(engine, _root, infoHash);
        torrents.Add(infoHash, torrent);
        torrent.Start();
        return torrent;
    }

    public IEnumerable<Torrent> Torrents => torrents.Values;
}

public class Torrent
{

    private MonoTorrent.Client.TorrentManager? _manager;
    private readonly MonoTorrent.Client.ClientEngine _engine;
    private readonly string _root;
    private readonly string _infoHash;

    public Torrent(MonoTorrent.Client.ClientEngine engine, string root, string infoHash)
    {
        _engine = engine;
        _root = root;
        _infoHash = infoHash;
    }

    public class TorrentFile
    {
        private readonly MonoTorrent.ITorrentManagerFile _file;

        public TorrentFile(MonoTorrent.ITorrentManagerFile file)
        {
            _file = file;
        }

        public string Path => _file.Path;
        public long Size => _file.Length;
        public long Downloaded => MonoTorrent.ITorrentFileInfoExtensions.BytesDownloaded(_file);
        public double Progress => 100.00 * Downloaded / Size;
    }

    public string Info_Hash => _infoHash;
    public string Name => _manager?.Torrent?.Name ?? "Loading...";
    public double Progress => _manager?.PartialProgress ?? 0.00;
    public double Seeders => _manager?.Peers?.Seeds ?? 0;
    public long TotalSize => _manager?.Torrent?.Size ?? 0;
    public long Downloaded => _manager?.Files.Sum(MonoTorrent.ITorrentFileInfoExtensions.BytesDownloaded) ?? 0;
    public IEnumerable<TorrentFile> Files => _manager?.Files.Select(file => new TorrentFile(file)).OrderBy(file => file.Path).ToArray() ?? new TorrentFile[0];

    public async void Start()
    {
        var torrent = await DownloadTorrentFileAsync();

        _manager = await _engine.AddAsync(torrent, _root);
        await _manager.StartAsync();

        double progress;
        do {
            Console.WriteLine($"{_infoHash}\t{_manager.State}\t{_manager.PartialProgress:F2}\t{_manager?.Torrent?.Name}");
            progress = _manager!.State switch
            {
                MonoTorrent.Client.TorrentState.Error => throw new Exception("Error downloading torrent", _manager.Error?.Exception),
                MonoTorrent.Client.TorrentState.Seeding => 100.00,
                _ => _manager.PartialProgress,
            };
            await Task.Delay(5_000);
        } while (progress < 99.999);

        await _manager.StopAsync();

        DeleteTorrentFile();
    }

    private string TorrentFilename => $"{_infoHash}.torrent";
    private string TorrentPath => Path.Join(_root, TorrentFilename);

    private async Task<MonoTorrent.Torrent> DownloadTorrentFileAsync()
    {
        if (File.Exists(TorrentPath)) return await MonoTorrent.Torrent.LoadAsync(TorrentPath);

        byte[] bytes;
        using (var client = new HttpClient())
        {
            var url = $"https://itorrents.org/torrent/{TorrentFilename}";
            try
            {
                bytes = await client.GetByteArrayAsync(url);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error downloading torrent file {url}: {ex.Message}");
            }
        }

        try
        {
            await File.WriteAllBytesAsync(TorrentPath, bytes);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error saving torrent file: {ex.Message}");
        }

        return await MonoTorrent.Torrent.LoadAsync(TorrentPath);
    }

    private void DeleteTorrentFile()
    {
        if (File.Exists(TorrentPath)) File.Delete(TorrentPath);
    }
}