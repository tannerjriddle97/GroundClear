namespace GroundClear;

public sealed record Location(double X, double Y, double Z, int Dimension);
public sealed record Drop(long Id, string Key, string Code, string Name, int Quantity, Location Position, byte[] Contents);
public sealed record ItemRow(string Key, string Code, string Name, long Quantity, int Stacks);
public sealed record Preview(string Token, long ExpiresAt, Drop[] Drops, ItemRow[] Rows);
public sealed record CleanupResult(long Quantity, int Stacks, int Skipped);

public interface IDropWorld
{
    IEnumerable<Drop> Scan(Location center);
    Drop? Get(long id);
    void Remove(long id);
}

/// <summary>The only deletion path. Both network requests and commands use this engine.</summary>
public sealed class CleanupEngine(IDropWorld world, Func<long> now)
{
    public const double Radius = 16;
    public const int BatchSize = 128;
    public const long PreviewLifetime = 60_000;
    private readonly Dictionary<string, Preview> previews = new();
    private readonly Dictionary<string, Job> jobs = new();
    public bool HasJobs => jobs.Count != 0;
    public bool IsBusy(string uid) => jobs.ContainsKey(uid);

    public static bool InRange(Location a, Location b) => a.Dimension == b.Dimension &&
        Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2) <= Radius * Radius;

    public static ItemRow[] Group(IEnumerable<Drop> drops) => drops.GroupBy(d => d.Key)
        .Select(g => new ItemRow(g.Key, g.First().Code, g.First().Name, g.Sum(d => (long)d.Quantity), g.Count()))
        .OrderByDescending(g => g.Quantity).ThenBy(g => g.Code, StringComparer.Ordinal).ToArray();

    private Drop[] Nearby(Location center) => world.Scan(center).Where(d => d.Quantity > 0 && InRange(center, d.Position)).ToArray();
    public ItemRow[] List(Location center) => Group(Nearby(center));

    public Preview Prepare(string uid, Location center, string[]? keys)
    {
        if (IsBusy(uid)) throw new InvalidOperationException("Cleanup is already running.");
        previews.Remove(uid);
        var selected = keys == null ? null : keys.ToHashSet(StringComparer.Ordinal);
        var drops = Nearby(center).Where(d => selected == null || selected.Contains(d.Key)).ToArray();
        if (drops.Length == 0) throw new InvalidOperationException("No matching loose drops within 16 blocks.");
        var preview = new Preview(Guid.NewGuid().ToString("N"), now() + PreviewLifetime, drops, Group(drops));
        previews[uid] = preview;
        return preview;
    }

    public void Confirm(string uid, string? token)
    {
        if (IsBusy(uid)) throw new InvalidOperationException("Cleanup is already running.");
        if (!previews.TryGetValue(uid, out var preview)) throw new InvalidOperationException("Prepare a cleanup preview first.");
        if (token != null && token != preview.Token) throw new InvalidOperationException("This preview was replaced. Prepare a new preview.");
        previews.Remove(uid);
        if (now() >= preview.ExpiresAt) throw new InvalidOperationException("Preview expired. Prepare a new preview.");
        jobs[uid] = new Job(preview.Drops);
    }

    public void Cancel(string uid) => previews.Remove(uid);
    public void Disconnect(string uid) { previews.Remove(uid); jobs.Remove(uid); }

    // One global budget, even if multiple admins confirm simultaneously.
    public Dictionary<string, CleanupResult> Advance(Func<string, Location?> authorizedPosition)
    {
        var completed = new Dictionary<string, CleanupResult>();
        int budget = BatchSize;
        foreach (var (uid, job) in jobs.ToArray())
        {
            var position = authorizedPosition(uid);
            if (position == null) { job.Skipped += job.Drops.Length - job.Index; job.Index = job.Drops.Length; }
            while (budget > 0 && job.Index < job.Drops.Length)
            {
                budget--;
                var expected = job.Drops[job.Index++];
                var actual = world.Get(expected.Id);
                if (actual == null || !InRange(position!, actual.Position) || actual.Key != expected.Key ||
                    actual.Quantity != expected.Quantity || !actual.Contents.AsSpan().SequenceEqual(expected.Contents))
                { job.Skipped++; continue; }
                world.Remove(actual.Id);
                job.Quantity += actual.Quantity;
                job.Stacks++;
            }
            if (job.Index == job.Drops.Length)
            {
                completed[uid] = new CleanupResult(job.Quantity, job.Stacks, job.Skipped);
                jobs.Remove(uid);
            }
            if (budget == 0) break;
        }
        return completed;
    }

    private sealed class Job(Drop[] drops)
    {
        public Drop[] Drops = drops;
        public int Index, Stacks, Skipped;
        public long Quantity;
    }
}
