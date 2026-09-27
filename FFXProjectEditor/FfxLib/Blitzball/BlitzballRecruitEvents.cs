using System.Collections.Generic;
using System.IO;

namespace FFXProjectEditor.FfxLib.Blitzball
{
    /// <summary>
    /// The blitzball recruitment field events (corpus-derived: every <c>.ebp</c> that contains the recruit-write
    /// signature <c>AE 28 00 14 AE &lt;playerId&gt; 00 A3</c> = append a player to YOUR team). The <c>EventId</c> is the
    /// authoritative key; <c>Area</c> is an approximate location convenience. The on-disk path is
    /// <c>&lt;event/obj&gt;/&lt;first2&gt;/&lt;eventId&gt;/&lt;eventId&gt;.ebp</c>. See BlitzballRecruit_File for the editing primitive.
    /// </summary>
    public static class BlitzballRecruitEvents
    {
        public static readonly IReadOnlyList<(string EventId, string Area)> Events = new[]
        {
            ("bsvr0400", "Besaid"),
            ("lchb0000", "Luca"), ("lchb0100", "Luca"), ("lchb0300", "Luca (Luca Goers)"),
            ("lchb0400", "Luca (Ronso Fangs)"), ("lchb0500", "Luca"), ("lchb0802", "Luca (story / Tidus)"),
            ("lchb0900", "Luca"), ("lchb1300", "Luca (Aurochs re-sign)"), ("lchb1800", "Luca"),
            ("luca0100", "Luca"), ("luca0400", "Luca"),
            ("ptkl0200", "Kilika"), ("ptkl0600", "Kilika"), ("ptkl0800", "Kilika"),
            ("ptkl1700", "Kilika"), ("ptkl1800", "Kilika"),
            ("swin0000", "S.S. Winno"), ("mihn0300", "Mi'ihen Highroad"), ("mcyt0000", "Macalania"),
            ("guad0000", "Guadosalam"), ("guad0100", "Guadosalam"), ("guad0300", "Guadosalam"), ("guad0400", "Guadosalam"),
            ("kami0100", "Thunder Plains"), ("genk1100", "Moonflow"),
            ("nagi0000", "Calm Lands"), ("nagi0400", "Calm Lands (Gorge)"),
            ("hiku0000", "Airship"), ("hiku0500", "Airship (Al Bhed Psyches)"), ("hiku0800", "Airship"),
            ("hiku0801", "Airship"), ("hiku1900", "Airship"),
            ("djyt0000", "Djose"),
        };

        /// <summary>On-disk path of a recruit event's <c>.ebp</c>, given the <c>event/obj</c> root.</summary>
        public static string EventPath(string eventObjRoot, string eventId)
            => Path.Combine(eventObjRoot, eventId.Substring(0, 2), eventId, eventId + ".ebp");
    }

    /// <summary>One selectable blitzball player (id + display label) for the recruit dropdown.</summary>
    public sealed class BlitzballPlayerOption
    {
        public required byte Id { get; init; }
        public required string Name { get; init; }
        public string Label => $"0x{Id:X2} · {Name}";
        public string SearchText => $"{Id:X2} {Id} {Name}";
    }
}
