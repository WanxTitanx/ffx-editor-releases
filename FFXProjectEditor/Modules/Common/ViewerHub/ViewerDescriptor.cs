using System.Collections.Generic;

namespace FFXProjectEditor.Modules.Common.ViewerHub
{
    /// <summary>
    /// 🐉 VIEWER DESCRIPTOR — registro declarativo de um viewer 3D do hub. Sem porta (o hub tem porta ÚNICA).
    /// O mesmo ViewerShell renderiza qualquer desc; os Tools definem as "pequenas exceções" do menu universal.
    /// </summary>
    public sealed record ViewerDescriptor(
        string Id,               // ex.: "monster-studio"
        string Title,            // ex.: "Monster Studio (PS2 3D)"
        string Host,             // metadado (o BuildUrl usa IP loopback direto)
        string Route,            // hash do app noclip, ex.: "#ffx/monster-studio" (vazio p/ web apps)
        string BundleDirectory,  // app-relative allowlist key: map/model/magic/noclip
        bool RequiresNoclip = true,
        bool RequiresProject = false,
        string? Path = null,     // path do web app, ex.: "/magic/index.html" (nulo = "/index.html" do noclip)
        IReadOnlyList<string>? Tools = null); // tools do menu universal por modo (F3+)
}
