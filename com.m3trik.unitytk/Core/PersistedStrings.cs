namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// The strings that outlive the code, declared once: files, scenes, clips and
    /// importer data that a project saves hold them. No project uses unitytk yet,
    /// so none is frozen; once one does, each is renamed only with an alias that
    /// reads the old one (base architecture, Data).
    /// </summary>
    public static class PersistedStrings
    {
        /// <summary>The scene's own records carrier node: every DCC export writes it, every importer reads it.</summary>
        public const string Carrier = "data_export";

        /// <summary>The suffix of a model's own media folder, beside the model.</summary>
        public const string MediaFolderSuffix = "_Media";

        /// <summary>The mark an extracted file's importer user data carries, naming the model it came from.</summary>
        public const string ExtractedByMark = "unitytk.extracted-by:";

        /// <summary>The memo of models whose embedded files were requested once (in Library, rebuilt with it).</summary>
        public const string EmbeddedRequestedMemo = "Library/unitytk/embedded_requested.txt";

        /// <summary>The function an AnimationEvent that starts a sound calls (the Animation Audio player's): saved in every clip that cues one, delivered or authored in Unity.</summary>
        public const string PlayAudioFunction = "PlayAudio";

        /// <summary>The function an AnimationEvent that stops a sound calls (the Animation Audio player's): saved beside its <see cref="PlayAudioFunction"/>.</summary>
        public const string StopAudioFunction = "StopAudio";
    }
}
