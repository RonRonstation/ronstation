using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{  
    /// <summary>
    ///     Swaps the emotes menu with an alternative menu
    /// </summary>
    public static readonly CVarDef<bool> AlternativeEmotesMenu =
        CVarDef.Create("hud.alternative_emotes_menu", false, CVar.CLIENTONLY | CVar.ARCHIVE);
}
