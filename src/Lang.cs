namespace DeepTools
{
    // Простая локализация: каждая строка задаётся парой (рус, англ)
    public static class Lang
    {
        public static bool IsEn = false;

        public static string T(string ru, string en)
        {
            return IsEn ? en : ru;
        }
    }
}
