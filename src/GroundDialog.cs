using Vintagestory.API.Client;

namespace GroundClear;

public sealed class GroundDialog : GuiDialog
{
    private readonly Action<GroundRequest> send;
    private readonly Action closed;
    private GroundRow[] rows = [];
    private readonly HashSet<string> selected = new(StringComparer.Ordinal);
    private string filter = "", token = "", message = "Loading nearby drops...";
    private bool preview, waiting, busy;
    private long searchRevision;
    private ElementBounds? scrollContent;
    public override string ToggleKeyCombinationCode => "";

    public GroundDialog(ICoreClientAPI api, Action<GroundRequest> send, Action closed) : base(api)
    { this.send = send; this.closed = closed; }

    public void ShowLoading()
    {
        rows = []; selected.Clear(); filter = ""; preview = false; busy = false; token = "";
        message = "Loading nearby drops..."; waiting = true; Compose();
    }

    public void Receive(GroundResponse response)
    {
        waiting = false;
        busy = response.Mode == "busy";
        if (response.Mode != "error")
        {
            rows = response.Rows ?? [];
            preview = response.Mode == "preview";
            token = response.Token;
            selected.Clear(); filter = "";
        }
        else { preview = false; token = ""; }
        message = response.Message;
        Compose();
    }

    private bool Send(GroundRequest request)
    {
        if (waiting || busy) return true;
        waiting = true;
        message = request.Action == "confirm" ? "Starting cleanup..." : "Waiting for server...";
        Compose(); send(request); return true;
    }

    private void Compose(bool focusSearch = false)
    {
        searchRevision++;
        SingleComposer?.Dispose();
        const int width = 650, viewHeight = 280;
        var dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, width + 40, 560);
        var inset = ElementBounds.Fixed(20, 40, width, 500);
        var composer = capi.Gui.CreateCompo("groundclear-window", dialogBounds)
            .AddShadedDialogBG(ElementBounds.Fill, true)
            .AddDialogTitleBar(preview ? "GroundClear - Confirm permanent deletion" : "GroundClear - Nearby loose drops", () => TryClose())
            .BeginChildElements(inset)
            .AddStaticText(Wrap(message, 84), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, width, 70));

        if (!preview)
        {
            composer.AddStaticText("Search", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 73, 60, 25))
                .AddTextInput(ElementBounds.Fixed(65, 68, 420, 30), text =>
                {
                    filter = text;
                    long revision = ++searchRevision;
                    capi.Event.RegisterCallback(_ => { if (revision == searchRevision && IsOpened()) Compose(true); }, 200);
                }, CairoFont.WhiteSmallText(), "search")
                .AddSmallButton("Refresh", () => Send(new() { Action = "list" }), ElementBounds.Fixed(505, 68, 135, 30), key: "refresh");
        }
        else composer.AddStaticText("Only the drops captured by this preview can be deleted.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 73, width, 30));

        var visible = rows.Where(r => preview || r.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || r.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var clip = ElementBounds.Fixed(0, 110, width - 25, viewHeight);
        scrollContent = ElementBounds.Fixed(0, 0, width - 25, Math.Max(viewHeight, visible.Length * 62));
        composer.BeginClip(clip).BeginChildElements(scrollContent);
        for (int i = 0; i < visible.Length; i++)
        {
            var row = visible[i];
            int x = preview ? 0 : 37;
            if (!preview)
            {
                composer.AddSwitch(on => { if (on) selected.Add(row.Key); else selected.Remove(row.Key); },
                    ElementBounds.Fixed(0, i * 62 + 6, 25, 25), "select-" + i, 25);
            }
            composer.AddStaticTextAutoFontSize(row.Name, CairoFont.WhiteSmallText(), ElementBounds.Fixed(x, i * 62, 350 - x, 25))
                .AddStaticTextAutoFontSize($"{row.Quantity:N0} items / {row.Stacks:N0} stacks", CairoFont.WhiteSmallText(), ElementBounds.Fixed(360, i * 62, 260, 25))
                .AddStaticTextAutoFontSize(row.Code, CairoFont.WhiteSmallText(), ElementBounds.Fixed(x, i * 62 + 26, width - 35 - x, 26))
                .AddHoverText(row.Name + "\n" + row.Code, CairoFont.WhiteSmallText(), 550, ElementBounds.Fixed(x, i * 62, width - 35 - x, 55));
        }
        if (visible.Length == 0)
            composer.AddStaticText(waiting || busy ? "Please wait..." : "No matching loose drops.", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, width - 35, 40));
        composer.EndChildElements().EndClip()
            .AddVerticalScrollbar(value =>
            {
                scrollContent.fixedY = -value;
                scrollContent.CalcWorldBounds();
            }, ElementBounds.Fixed(width - 20, 110, 20, viewHeight), "scroll");

        if (preview)
        {
            composer.AddSmallButton("Delete permanently", () => Send(new() { Action = "confirm", Token = token }), ElementBounds.Fixed(0, 410, 225, 35), key: "delete")
                .AddSmallButton("Cancel", () => Send(new() { Action = "cancel" }), ElementBounds.Fixed(240, 410, 140, 35), key: "cancel");
        }
        else
        {
            composer.AddSmallButton("Clear selected", () =>
            {
                if (selected.Count == 0) { message = "Select at least one item type first."; Compose(); return true; }
                return Send(new() { Action = "preview", Keys = selected.ToArray() });
            }, ElementBounds.Fixed(0, 410, 180, 35), key: "selected")
                .AddSmallButton("Clear all", () => Send(new() { Action = "preview", All = true }), ElementBounds.Fixed(195, 410, 150, 35), key: "all");
        }
        composer.AddSmallButton("Close", () => TryClose(), ElementBounds.Fixed(505, 410, 135, 35))
            .AddStaticText(preview ? "Permanent deletion. No undo. Cancel to keep all drops." : "16-block sphere. Inventory, containers and placed piles are unaffected.",
                CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 465, width, 30))
            .EndChildElements().Compose();
        SingleComposer = composer;
        composer.GetScrollbar("scroll").SetHeights(viewHeight, Math.Max(viewHeight, visible.Length * 62));
        if (!preview)
        {
            composer.GetTextInput("search").SetValue(filter, false);
            for (int i = 0; i < visible.Length; i++) composer.GetSwitch("select-" + i).SetValue(selected.Contains(visible[i].Key));
            composer.GetButton("refresh").Enabled = !waiting && !busy;
            composer.GetButton("selected").Enabled = !waiting && !busy;
            composer.GetButton("all").Enabled = !waiting && !busy;
            if (focusSearch) composer.FocusElement(composer.GetTextInput("search").TabIndex);
        }
        else
        {
            composer.GetButton("delete").Enabled = !waiting && !busy;
            composer.GetButton("cancel").Enabled = !waiting && !busy;
        }
    }

    public override void OnGuiClosed()
    {
        searchRevision++;
        closed();
        if (preview && !waiting && !busy) send(new() { Action = "cancel" });
        base.OnGuiClosed();
    }

    private static string Wrap(string value, int width)
    {
        var words = value.Split(' '); var result = new System.Text.StringBuilder(); int col = 0;
        foreach (var word in words) { if (col + word.Length + 1 > width) { result.Append('\n'); col = 0; } else if (col > 0) { result.Append(' '); col++; } result.Append(word); col += word.Length; }
        return result.ToString();
    }
}
