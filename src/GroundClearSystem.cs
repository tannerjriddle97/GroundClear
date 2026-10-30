using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace GroundClear;

public sealed class GroundClearSystem : ModSystem
{
    private ICoreServerAPI? server;
    private IServerNetworkChannel? serverChannel;
    private CleanupEngine? engine;
    private long tickId;
    private readonly HashSet<string> windowJobs = new();
    private readonly Dictionary<string, long> lastRequests = new();
    private ICoreClientAPI? client;
    private IClientNetworkChannel? clientChannel;
    private GroundDialog? dialog;
    private bool wantsWindow;

    public override void StartServerSide(ICoreServerAPI api)
    {
        server = api;
        engine = new CleanupEngine(new VintageDropWorld(api.World), () => Environment.TickCount64);
        serverChannel = api.Network.RegisterChannel("groundclear")
            .RegisterMessageType<GroundRequest>().RegisterMessageType<GroundResponse>()
            .SetMessageHandler<GroundRequest>((player, request) =>
                api.Event.EnqueueMainThreadTask(() => HandleRequest(player, request), "groundclear-request"));
        api.Event.PlayerDisconnect += OnDisconnect;
        var root = api.ChatCommands.Create("groundclear").WithDescription("Preview and clear loose drops within 16 blocks")
            .RequiresPlayer().RequiresPrivilege("controlserver");
        root.BeginSubCommand("list").WithDescription("List nearby loose item types")
            .HandleWith(args => Command(args, "list")).EndSubCommand();
        root.BeginSubCommand("preview").WithDescription("Preview all or one exact item code")
            .WithArgs(api.ChatCommands.Parsers.Word("all|item-code"))
            .HandleWith(args => Command(args, "preview", (string)args[0])).EndSubCommand();
        root.BeginSubCommand("confirm").WithDescription("Permanently delete the latest preview")
            .HandleWith(args => Command(args, "confirm")).EndSubCommand();
        root.BeginSubCommand("cancel").WithDescription("Discard the pending preview")
            .HandleWith(args => Command(args, "cancel")).EndSubCommand();
        root.HandleWith(_ => TextCommandResult.Success("Use /groundclear list, preview all, preview <item-code>, confirm, or cancel. Open the window with .groundclear."));
    }

    private static bool Authorized(IServerPlayer p) => p.HasPrivilege("controlserver");
    private static Location Position(IServerPlayer p) => VintageDropWorld.Position(p.Entity.Pos);
    private static GroundRow[] Rows(ItemRow[] rows) => rows.Select(GroundRow.From).ToArray();

    private GroundResponse Execute(IServerPlayer player, GroundRequest request, bool window)
    {
        if (!Authorized(player)) throw new InvalidOperationException("GroundClear requires server-admin permission (controlserver).");
        var uid = player.PlayerUID;
        if (engine!.IsBusy(uid)) throw new InvalidOperationException("Cleanup is already running. Please wait for the result.");
        switch (request.Action)
        {
            case "list":
                return new() { Rows = Rows(engine.List(Position(player))), Message = "Loose drops within 16 blocks. Select item types to clear." };
            case "preview":
                engine.Cancel(uid);
                if (!request.All && (request.Keys == null || request.Keys.Length == 0))
                    throw new InvalidOperationException("Select at least one item type first.");
                if (request.Keys?.Length > 4096) throw new InvalidOperationException("Too many selected types.");
                var preview = engine.Prepare(uid, Position(player), request.All ? null : request.Keys);
                return new() { Mode = "preview", Rows = Rows(preview.Rows), Token = preview.Token,
                    Message = $"Permanently delete {preview.Rows.Sum(r => r.Quantity):N0} items in {preview.Drops.Length:N0} dropped stacks? Preview expires in 60 seconds." };
            case "confirm":
                if (window && string.IsNullOrWhiteSpace(request.Token)) throw new InvalidOperationException("Missing preview. Refresh and try again.");
                engine.Confirm(uid, window ? request.Token : null);
                if (window) windowJobs.Add(uid);
                if (tickId == 0) tickId = server!.Event.RegisterGameTickListener(Tick, 20);
                return new() { Mode = "busy", Message = "Clearing previewed drops..." };
            case "cancel":
                engine.Cancel(uid);
                return new() { Rows = Rows(engine.List(Position(player))), Message = "Preview cancelled. Nothing was removed." };
            default: throw new InvalidOperationException("Unknown GroundClear action.");
        }
    }

    private void HandleRequest(IServerPlayer player, GroundRequest request)
    {
        try
        {
            // Bound inbound work without running any background scans.
            long now = Environment.TickCount64;
            if (lastRequests.TryGetValue(player.PlayerUID, out var last) && now - last < 200)
            { Send(player, new() { Mode = "error", Message = "Please wait a moment and try again." }); return; }
            lastRequests[player.PlayerUID] = now;
            Send(player, Execute(player, request, true));
        }
        catch (Exception e) { server!.Logger.Warning("GroundClear request: {0}", e.Message); Send(player, new() { Mode = "error", Message = e.Message }); }
    }

    private TextCommandResult Command(TextCommandCallingArgs args, string action, string? code = null)
    {
        if (args.Caller.Player is not IServerPlayer player) return TextCommandResult.Error("Run this command as a player.");
        try
        {
            var request = new GroundRequest { Action = action, All = code == "all" };
            if (action == "preview" && code != "all")
            {
                // Only exact codes, never substring or wildcard matches.
                string canonical = code!.Contains(':') ? code : "game:" + code;
                request.Keys = engine!.List(Position(player)).Where(r => r.Code == canonical || r.Key == code).Select(r => r.Key).ToArray();
            }
            var response = Execute(player, request, false);
            string rows = string.Join("\n", response.Rows.Select(r => $"{r.Name}: {r.Quantity:N0} items / {r.Stacks:N0} stacks [{r.Code}]"));
            string confirm = response.Mode == "preview" ? "\nUse /groundclear confirm to delete, or /groundclear cancel." : "";
            return TextCommandResult.Success(response.Message + (rows.Length > 0 ? "\n" + rows : action == "list" ? "\nNo matching drops." : "") + confirm);
        }
        catch (Exception e) { return TextCommandResult.Error(e.Message); }
    }

    private void Tick(float dt)
    {
        foreach (var (uid, result) in engine!.Advance(uid =>
        {
            var p = server!.World.PlayerByUid(uid) as IServerPlayer;
            return p?.Entity != null && Authorized(p) ? Position(p) : null;
        }))
        {
            var p = server!.World.PlayerByUid(uid) as IServerPlayer;
            string message = $"GroundClear removed {result.Quantity:N0} items in {result.Stacks:N0} dropped stacks. Skipped {result.Skipped:N0} changed, missing, or out-of-range stacks.";
            if (p != null)
            {
                p.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.CommandSuccess);
                if (windowJobs.Remove(uid)) Send(p, new() { Rows = Rows(engine.List(Position(p))), Message = message });
            }
            server.Logger.Notification("{0}: {1}", uid, message);
        }
        if (!engine.HasJobs && tickId != 0) { server!.Event.UnregisterGameTickListener(tickId); tickId = 0; }
    }

    private void Send(IServerPlayer p, GroundResponse r) => serverChannel!.SendPacket(r, p);
    private void OnDisconnect(IServerPlayer p) { engine!.Disconnect(p.PlayerUID); windowJobs.Remove(p.PlayerUID); lastRequests.Remove(p.PlayerUID); }

    public override void StartClientSide(ICoreClientAPI api)
    {
        client = api;
        clientChannel = api.Network.RegisterChannel("groundclear")
            .RegisterMessageType<GroundRequest>().RegisterMessageType<GroundResponse>()
            .SetMessageHandler<GroundResponse>(response => api.Event.EnqueueMainThreadTask(() =>
            {
                if (!wantsWindow) return;
                dialog ??= new GroundDialog(api, Request, () => wantsWindow = false);
                dialog.Receive(response);
                dialog.TryOpen();
            }, "groundclear-response"));
        api.ChatCommands.Create("groundclear").WithDescription("View and clear loose drops within 16 blocks")
            .HandleWith(_ =>
            {
                if (!clientChannel.Connected) return TextCommandResult.Error("GroundClear must also be installed on the server.");
                wantsWindow = true;
                dialog ??= new GroundDialog(api, Request, () => wantsWindow = false);
                dialog.ShowLoading(); dialog.TryOpen();
                Request(new() { Action = "list" });
                return TextCommandResult.Success("Opening GroundClear...");
            });
    }

    private void Request(GroundRequest request)
    {
        if (clientChannel?.Connected == true) clientChannel.SendPacket(request);
        else dialog?.Receive(new() { Mode = "error", Message = "GroundClear server connection is unavailable." });
    }

    public override void Dispose()
    {
        if (server != null)
        {
            server.Event.PlayerDisconnect -= OnDisconnect;
            if (tickId != 0) server.Event.UnregisterGameTickListener(tickId);
        }
        dialog?.Dispose();
        base.Dispose();
    }
}
