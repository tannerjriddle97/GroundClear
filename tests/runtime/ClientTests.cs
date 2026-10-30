using GroundClear;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace GroundClearRuntimeTests;

// Composes off-screen GUI elements using the actual game renderer. Does not open,
// click, or operate the game's UI. This is an automated rendering/API smoke test.
public sealed class ClientTests : ModSystem
{
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.LevelFinalize += () => api.Event.EnqueueMainThreadTask(() =>
        {
            try
            {
                var rows = Enumerable.Range(0, 100).Select(i => new GroundRow
                {
                    Key = "Item:game:test-" + i, Code = "game:test-" + i, Name = i == 0 ? "Quartz chunks" : "Test drop " + i,
                    Quantity = i == 0 ? 2000 : 3, Stacks = i == 0 ? 2000 : 1
                }).ToArray();
                using var dialog = new GroundDialog(api, _ => throw new Exception("Unexpected request during composition"), () => { });
                dialog.ShowLoading();
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST PASS: loading window composes");
                dialog.Receive(new() { Rows = rows, Message = "Loose drops within 16 blocks. Select item types to clear." });
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST PASS: 100-row scrollable item window composes");
                dialog.Receive(new() { Mode = "preview", Token = "test", Rows = rows, Message = "Permanently delete 2,000 items in 2,000 dropped stacks? Preview expires in 60 seconds." });
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST PASS: confirmation window composes");
                dialog.Receive(new() { Mode = "busy", Message = "Clearing previewed drops..." });
                dialog.Receive(new() { Mode = "error", Message = "Preview expired. Prepare a new preview." });
                dialog.Receive(new() { Message = "No matching loose drops within 16 blocks." });
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST PASS: busy/error/empty states compose");
                if (!api.World.Player.HasPrivilege("controlserver")) throw new Exception("Single-player owner lacks controlserver privilege");
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST PASS: single-player owner has required cleanup privilege");
                api.Logger.Notification("GROUNDCLEAR_CLIENT_TEST COMPLETE");
            }
            catch (Exception e) { api.Logger.Error("GROUNDCLEAR_CLIENT_TEST FAIL: {0}", e); }
        }, "groundclear-client-tests");
    }
}
