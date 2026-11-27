namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>Which values keyed in an Answer row takes (<see cref="TaskSettings.answerKind"/>).</summary>
    public enum AnswerKind
    {
        /// <summary>The row's own value.</summary>
        Value = 0,
        /// <summary>Any value no other Answer row of the step names.</summary>
        Otherwise = 1,
    }
}
