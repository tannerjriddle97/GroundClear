using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace GroundClear;

public sealed class VintageDropWorld(IWorldAccessor world) : IDropWorld
{
    public static Location Position(EntityPos p) => new(p.X, p.Y, p.Z, p.Dimension);

    public IEnumerable<Drop> Scan(Location center)
    {
        var pos = new EntityPos(center.X, center.Y, center.Z) { Dimension = center.Dimension };
        // Slightly widen the broad query so the exact 16-block boundary is not
        // lost to the engine's spatial-query edge comparisons. InRange is final.
        return world.GetEntitiesAround(pos.XYZ, 16.01f, 16.01f, e => e is EntityItem)
            .OfType<EntityItem>().Select(Snapshot).OfType<Drop>();
    }

    public Drop? Get(long id) => world.GetEntityById(id) is EntityItem item ? Snapshot(item) : null;

    public void Remove(long id)
    {
        if (world.GetEntityById(id) is EntityItem item && item.Alive)
            item.Die(EnumDespawnReason.Removed);
        else throw new InvalidOperationException("Drop disappeared before removal.");
    }

    public static Drop? Snapshot(EntityItem item)
    {
        var stack = item.Itemstack;
        if (!item.Alive || stack?.Collectible?.Code == null || stack.StackSize <= 0) return null;
        var code = stack.Collectible.Code.ToString();
        return new Drop(item.EntityId, stack.Class + ":" + code, code, stack.GetName(), stack.StackSize,
            Position(item.Pos), stack.ToBytes());
    }
}
