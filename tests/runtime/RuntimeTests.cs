using GroundClear;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace GroundClearRuntimeTests;

// Test-only mod: never included in the release ZIP. Only use in a disposable world.
public sealed class RuntimeTests : ModSystem
{
    private ICoreServerAPI api = null!;
    private CleanupEngine engine = null!;
    private Location center = null!;
    private InventoryGeneric inventory = null!;
    private readonly List<long> quartzIds = new();
    private long silverId, outsideId;
    private int stage, ticks, checks;
    private EntityItem? attributeDrop;
    private int baseline;

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => Guard(() =>
        {
            var spawn = api.World.DefaultSpawnPosition;
            center = new Location(spawn.X + 16, 210, spawn.Z + 16, 0);
            api.WorldManager.LoadChunkColumnPriority((int)center.X / 32, (int)center.Z / 32,
                new ChunkLoadOptions { KeepLoaded = true, OnLoaded = () => api.Event.EnqueueMainThreadTask(() => Guard(Setup), "groundclear-runtime-setup") });
        }));
    }

    private void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++; api.Logger.Notification("GROUNDCLEAR_TEST PASS: {0}", name);
    }

    private EntityItem Spawn(Item item, double x, double y, double z, int count = 1)
    {
        return api.World.SpawnItemEntity(new ItemStack(item, count), new Vec3d(x, y, z), new Vec3d()) as EntityItem
            ?? throw new Exception("Could not spawn test item");
    }

    private void Setup()
    {
        var quartz = api.World.Items.FirstOrDefault(i => i.Code?.ToString() == "game:ore-quartz")
            ?? api.World.Items.First(i => i.Code?.Path.Contains("quartz") == true);
        var silver = api.World.Items.First(i => i.Code?.Path.StartsWith("ore-") == true && i.Code.Path.Contains("nativesilver"));
        api.Logger.Notification("GROUNDCLEAR_TEST items: {0}, {1}", quartz.Code, silver.Code);
        engine = new CleanupEngine(new VintageDropWorld(api.World), () => Environment.TickCount64);
        inventory = new InventoryGeneric(1, "groundclear-test", api);
        inventory[0].Itemstack = new ItemStack(quartz, 42);

        if (api.WorldManager.SaveGame.GetData("groundclear-tested") != null)
        {
            // Reload pass: load persisted nearby chunk and look for removed quartz IDs.
            var rows = engine.List(center);
            Check(rows.All(r => r.Code != quartz.Code.ToString()), "deleted quartz stays deleted after save/reload");
            api.Logger.Notification("GROUNDCLEAR_TEST RELOAD COMPLETE: {0} checks", checks);
            api.Server.ShutDown(); return;
        }

        // Real placed chest with a quartz stack, outside the loose-drop entity system.
        var chest = api.World.Blocks.First(b => b.Code?.Path.StartsWith("chest-") == true && b.Code.Path.Contains("north"));
        var chestPos = new BlockPos((int)center.X + 2, (int)center.Y - 3, (int)center.Z + 2, 0);
        api.World.BlockAccessor.SetBlock(chest.Id, chestPos);
        var chestEntity = api.World.BlockAccessor.GetBlockEntity(chestPos);
        Check(chestEntity is IBlockEntityContainer, "placed chest exposes an inventory");
        ((IBlockEntityContainer)chestEntity!).Inventory[0].Itemstack = new ItemStack(quartz, 23);
        chestEntity!.MarkDirty(true);
        testChest = chestPos;

        for (int i = 0; i < 2000; i++)
        {
            // Separate drops spatially to avoid natural stack merging during the test.
            var entity = Spawn(quartz, center.X - 6 + i % 13, center.Y - 6 + i / 169, center.Z - 6 + i / 13 % 13);
            quartzIds.Add(entity.EntityId);
        }
        silverId = Spawn(silver, center.X, center.Y, center.Z + 10, 3).EntityId;
        outsideId = Spawn(quartz, center.X + 20, center.Y, center.Z).EntityId;
        attributeDrop = Spawn(quartz, center.X + 10, center.Y, center.Z);
        api.Event.RegisterGameTickListener(dt => Guard(Tick), 20);
    }

    private BlockPos? testChest;

    private void Tick()
    {
        if (stage == 0)
        {
            // Let the real server add spawned entities to its spatial index first.
            if (++ticks < 5) return;
            var rows = engine.List(center);
            Check(rows.Any(r => r.Code.Contains("silver")), "real scanner sees silver drops");
            var quartzRow = rows.First(r => r.Code.Contains("quartz"));
            Check(quartzRow.Quantity == 2001 && quartzRow.Stacks == 2001, "real scanner finds 2,001 separate quartz entities");
            var preview = engine.Prepare("test", center, [quartzRow.Key]);
            attributeDrop!.Itemstack.Attributes.SetInt("groundclear-test-changed", 1);
            engine.Confirm("test", preview.Token);
            stage = 1; ticks = 0; baseline = quartzIds.Count + 1;
            return;
        }
        if (stage == 1)
        {
            var start = System.Diagnostics.Stopwatch.StartNew();
            var done = engine.Advance(_ => center);
            start.Stop();
            int alive = quartzIds.Count(id => api.World.GetEntityById(id)?.Alive == true) + (attributeDrop!.Alive ? 1 : 0);
            Check(baseline - alive <= 128, "real per-tick deletion budget " + ticks);
            baseline = alive; ticks++;
            api.Logger.Notification("GROUNDCLEAR_TEST batch time: {0} ms", start.Elapsed.TotalMilliseconds);
            if (done.TryGetValue("test", out var result))
            {
                Check(result.Quantity == 2000 && result.Stacks == 2000 && result.Skipped == 1, "real 2,000 quartz cleanup / changed attributes skipped");
                Check(ticks == 16, "real cleanup completes in 16 batches");
                Check(api.World.GetEntityById(silverId)?.Alive == true, "real silver survives quartz cleanup");
                Check(api.World.GetEntityById(outsideId)?.Alive == true, "real out-of-range quartz survives");
                Check(inventory[0].StackSize == 42, "inventory quartz remains intact");
                Check(((IBlockEntityContainer)api.World.BlockAccessor.GetBlockEntity(testChest!)).Inventory[0].StackSize == 23, "placed chest contents remain intact");
                // Clear the intentionally skipped/out-of-range test drops before persistence verification.
                attributeDrop.Die(EnumDespawnReason.Removed);
                (api.World.GetEntityById(outsideId) as EntityItem)?.Die(EnumDespawnReason.Removed);
                api.WorldManager.SaveGame.StoreData("groundclear-tested", [1]);
                stage = 2; ticks = 0;
            }
            return;
        }
        if (stage == 2 && ++ticks >= 5)
        {
            Check(quartzIds.All(id => api.World.GetEntityById(id)?.Alive != true), "removed entities leave the live world");
            api.Logger.Notification("GROUNDCLEAR_TEST COMPLETE: {0} checks", checks);
            stage = 3; api.Server.ShutDown();
        }
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) { api.Logger.Error("GROUNDCLEAR_TEST FAIL: {0}", e); api.Server.ShutDown(); }
    }
}
