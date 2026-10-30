using GroundClear;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
Location origin = new(0, 0, 0, 0);
Drop D(long id, string code = "quartz", int count = 1, double x = 0, double y = 0, double z = 0, int dimension = 0) =>
    new(id, "Item:game:" + code, "game:" + code, code, count, new(x, y, z, dimension), [(byte)count, 7]);
var world = new FakeWorld(); long time = 0;
var engine = new CleanupEngine(world, () => time);
Check(engine.List(origin).Length == 0, "empty list");
Reject(() => engine.Prepare("p", origin, null), "empty preview rejected");
foreach (var p in new[] { new Location(16, 0, 0, 0), new Location(0, -16, 0, 0), new Location(0, 0, 16, 0) })
    Check(CleanupEngine.InRange(origin, p), "16-block boundary included " + p);
foreach (var p in new[] { new Location(16.001, 0, 0, 0), new Location(0, -16.001, 0, 0), new Location(12, 12, 0, 0), new Location(0, 0, 0, 1) })
    Check(!CleanupEngine.InRange(origin, p), "outside sphere/dimension excluded " + p);
world.Drops[1] = D(1, count: 64); world.Drops[2] = D(2, "silver", 8);
world.Drops[3] = D(3, count: 32); world.Drops[4] = D(4, x: 17);
world.Drops[5] = D(5, dimension: 1);
var rows = engine.List(origin);
Check(rows.Length == 2 && rows[0].Quantity == 96 && rows[0].Stacks == 2, "grouping, counts, quantity sorting, range");
var preview = engine.Prepare("p", origin, ["Item:game:quartz"]);
Check(preview.Rows.Length == 1 && preview.Drops.Length == 2, "exact quartz selection");
engine.Cancel("p"); Reject(() => engine.Confirm("p", preview.Token), "cancel prevents deletion");
Check(world.Drops.Count == 5, "cancel leaves all drops");
preview = engine.Prepare("p", origin, null); time = preview.ExpiresAt;
Reject(() => engine.Confirm("p", preview.Token), "preview expires at 60 seconds");
preview = engine.Prepare("p", origin, null);
Reject(() => engine.Confirm("p", "wrong-token"), "wrong/stale token rejected");
engine.Confirm("p", preview.Token);
Reject(() => engine.Confirm("p", preview.Token), "duplicate confirmation rejected");
Reject(() => engine.Prepare("p", origin, null), "preview blocked during cleanup");
engine.Advance(_ => origin);
Reject(() => engine.Confirm("p", preview.Token), "confirmation cannot replay after completion");

world.Drops.Clear();
for (int i = 1; i <= 8; i++) world.Drops[i] = D(i);
world.Drops[9] = D(9, "silver");
preview = engine.Prepare("p", origin, ["Item:game:quartz"]);
world.Drops.Remove(1); // picked up
world.Drops[2] = D(2, count: 2); // merged/grown
world.Drops[3] = D(3, x: 17); // moved away
world.Drops[4] = D(4, dimension: 1);
world.Drops[5] = D(5, "silver"); // item changed
world.Drops[6] = D(6) with { Contents = [1, 99] }; // attributes/durability changed
world.Drops[10] = D(10); // new drop after preview
engine.Confirm("p", preview.Token);
var result = engine.Advance(_ => origin)["p"];
Check(result == new CleanupResult(2, 2, 6), "missing, merged, moved, dimension, item and attribute changes skipped");
Check(world.Drops.ContainsKey(9) && world.Drops.ContainsKey(10), "silver and new quartz survive");

world.Drops.Clear();
for (int i = 0; i < 2000; i++) world.Drops[i] = D(i);
world.Drops[3000] = D(3000, "silver");
preview = engine.Prepare("p", origin, ["Item:game:quartz"]);
engine.Confirm("p", preview.Token);
int ticks = 0; CleanupResult? final = null;
while (engine.HasJobs)
{
    int before = world.Removed;
    var done = engine.Advance(_ => origin);
    Check(world.Removed - before <= 128, "per-tick budget " + ticks);
    if (done.TryGetValue("p", out var value)) final = value;
    ticks++;
}
Check(ticks == 16 && final == new CleanupResult(2000, 2000, 0), "2,000-drop cleanup completes in 16 batches");
Check(world.Drops.Count == 1 && world.Drops.ContainsKey(3000), "2,000 quartz removed, silver preserved");

world.Drops.Clear(); world.Drops[1] = D(1);
preview = engine.Prepare("p", origin, null); engine.Confirm("p", preview.Token);
result = engine.Advance(_ => null)["p"];
Check(result.Stacks == 0 && result.Skipped == 1 && world.Drops.Count == 1, "disconnect/permission loss aborts remaining deletion");
preview = engine.Prepare("p", origin, null); engine.Confirm("p", preview.Token);
engine.Disconnect("p"); Check(!engine.HasJobs, "disconnect releases job state");
preview = engine.Prepare("p", origin, null); engine.Confirm("p", preview.Token);
result = engine.Advance(_ => new Location(100, 0, 0, 0))["p"];
Check(result.Stacks == 0 && result.Skipped == 1, "player movement revalidates range");

world.Drops.Clear();
for (int i = 0; i < 300; i++) world.Drops[i] = D(i);
engine.Prepare("p", origin, null); engine.Confirm("p", null);
engine.Prepare("q", origin, null); engine.Confirm("q", null);
int beforeMulti = world.Removed;
engine.Advance(_ => origin);
Check(world.Removed - beforeMulti == 128, "batch budget is global across simultaneous jobs");
Console.WriteLine($"ALL {checks} CHECKS PASSED");

sealed class FakeWorld : IDropWorld
{
    public readonly Dictionary<long, Drop> Drops = new();
    public int Removed;
    public IEnumerable<Drop> Scan(Location center) => Drops.Values.ToArray();
    public Drop? Get(long id) => Drops.GetValueOrDefault(id);
    public void Remove(long id) { Drops.Remove(id); Removed++; }
}
