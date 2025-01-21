namespace CounterPartMusic.Extensions
{

    public static class StringExtensions
    {
        public static string ForgivingSubstring(this string str, int start, int length)
        {
            if (str == null)
            {
                return string.Empty;
            }

            if (start < 0)
            {
                start = 0;
            }

            if (start > str.Length)
            {
                return string.Empty;
            }

            if (length < 0)
            {
                length = 0;
            }

            if (start + length > str.Length)
            {
                length = str.Length - start;
            }

            return str.Substring(start, length);
        }
    }

}
