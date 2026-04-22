using System;
using ForgeMap;

public class Event { public DateTimeOffset? When { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Event.When))]
    public partial Event? ForgeEvent(DateTime? source);
}
