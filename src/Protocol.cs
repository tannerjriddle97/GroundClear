using ProtoBuf;

namespace GroundClear;

[ProtoContract]
public sealed class GroundRequest
{
    [ProtoMember(1)] public string Action { get; set; } = "list";
    [ProtoMember(2)] public string[] Keys { get; set; } = [];
    [ProtoMember(3)] public bool All { get; set; }
    [ProtoMember(4)] public string Token { get; set; } = "";
}

[ProtoContract]
public sealed class GroundRow
{
    [ProtoMember(1)] public string Key { get; set; } = "";
    [ProtoMember(2)] public string Code { get; set; } = "";
    [ProtoMember(3)] public string Name { get; set; } = "";
    [ProtoMember(4)] public long Quantity { get; set; }
    [ProtoMember(5)] public int Stacks { get; set; }
    public static GroundRow From(ItemRow r) => new() { Key = r.Key, Code = r.Code, Name = r.Name, Quantity = r.Quantity, Stacks = r.Stacks };
}

[ProtoContract]
public sealed class GroundResponse
{
    [ProtoMember(1)] public string Mode { get; set; } = "list";
    [ProtoMember(2)] public string Message { get; set; } = "";
    [ProtoMember(3)] public string Token { get; set; } = "";
    [ProtoMember(4)] public GroundRow[] Rows { get; set; } = [];
}
