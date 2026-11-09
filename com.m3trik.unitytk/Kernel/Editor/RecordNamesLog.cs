using M3trik.Unitytk.Core;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// What an importer says when a record's name binds nothing (<see cref="RecordNames"/>):
    /// one wording for every reader of a delivery, a tie never guessed and named, an absent
    /// name's object asked for.
    /// </summary>
    /// <remarks>
    /// Prior art: each importer phrased its own (four wordings of one rule); the rule is the
    /// core's, so its report sits beside it here, where the import log is.
    /// </remarks>
    public static class RecordNamesLog
    {
        /// <summary>
        /// The report of <paramref name="what"/> (<c>shadow plane 'x''s contact 'y'</c>), whose
        /// name came out <paramref name="outcome"/> among <paramref name="candidates"/>, and
        /// what the import does instead (<paramref name="fallback"/>: <c>it is left out</c>);
        /// null for a bound name.
        /// </summary>
        public static string Message(string what, RecordNames.Outcome outcome, string[] candidates, string fallback)
        {
            switch (outcome)
            {
                case RecordNames.Outcome.Ambiguous:
                    return $"{what} matches several objects equally ({string.Join(", ", candidates ?? new string[0])}): " +
                           $"{fallback}. Give them names (or parents) that tell them apart, and re-export.";
                case RecordNames.Outcome.Absent:
                    return $"{what} is not in the file: {fallback}. Include it in the export.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Say <see cref="Message"/> in the import log of <paramref name="assetPath"/>, as a
        /// warning; nothing for a bound name.
        /// </summary>
        public static void Unbound(string assetPath, string what, RecordNames.Outcome outcome, string[] candidates,
                                   string fallback)
        {
            string message = Message(what, outcome, candidates, fallback);
            if (message != null) ImportLog.Warning(assetPath, message);
        }
    }
}
