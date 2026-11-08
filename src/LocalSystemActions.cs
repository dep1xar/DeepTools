using System;
using System.IO;

namespace DeepTools
{
    public static class LocalSystemActions
    {
        // Основной метод для полной очистки временных папок
        public static long CleanTempDirectories()
        {
            long totalFreedBytes = 0;
            
            // 1. Очистка Temp пользователя (не требует прав администратора)
            string userTemp = Path.GetTempPath();
            totalFreedBytes += DeleteFolderContents(userTemp);

            // 2. Очистка Windows Temp (требует запуска от имени администратора)
            try
            {
                string winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
                totalFreedBytes += DeleteFolderContents(winTemp);
            }
            catch (UnauthorizedAccessException)
            {
                // Логируем отсутствие прав админа, если приложение запущено без UAC
            }

            // 3. Очистка Prefetch (требует запуска от имени администратора)
            try
            {
                string prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
                totalFreedBytes += DeleteFolderContents(prefetch);
            }
            catch (UnauthorizedAccessException) { }

            return totalFreedBytes;
        }

        // Рекурсивное удаление содержимого папки
        private static long DeleteFolderContents(string path)
        {
            long freedBytes = 0;
            if (!Directory.Exists(path)) return 0;

            // Удаляем файлы в текущей директории
            foreach (string file in Directory.GetFiles(path))
            {
                try
                {
                    FileInfo fi = new FileInfo(file);
                    long size = fi.Length;
                    File.Delete(file);
                    freedBytes += size;
                }
                catch
                {
                    // Файл может быть заблокирован запущенным процессом - это нормально, пропускаем его
                }
            }

            // Рекурсивно очищаем и удаляем подпапки
            foreach (string dir in Directory.GetDirectories(path))
            {
                try
                {
                    freedBytes += DeleteFolderContents(dir);
                    Directory.Delete(dir, false);
                }
                catch
                {
                    // Папка занята - оставляем как есть
                }
            }

            return freedBytes;
        }
    }
}   