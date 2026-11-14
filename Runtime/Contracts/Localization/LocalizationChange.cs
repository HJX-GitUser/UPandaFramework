namespace UPandaGF
{
    /// <summary>当前语言变化事件。</summary>
    public readonly struct LocalizationChange
    {
        public readonly string PreviousLocale;
        public readonly string CurrentLocale;

        public LocalizationChange(string previousLocale, string currentLocale)
        {
            PreviousLocale = previousLocale;
            CurrentLocale = currentLocale;
        }

        public override string ToString()
        {
            return PreviousLocale + " -> " + CurrentLocale;
        }
    }
}
