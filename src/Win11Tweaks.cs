using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace DeepTools
{
    // Тумблеры «дебloat» Windows 11: реклама/подсказки, Copilot, Recall, виджеты,
    // Bing в поиске. Всё через HKCU-реестр, полностью обратимо.
    public static class Win11Tweaks
    {
        public class RegEntry
        {
            public string Path, Name;
            public int DebloatVal, DefaultVal;
            public RegEntry(string path, string name, int debloat, int def)
            { Path = path; Name = name; DebloatVal = debloat; DefaultVal = def; }
        }

        public class Tweak
        {
            public string Title, Desc;
            public RegEntry[] Entries;

            public bool IsApplied()
            {
                try
                {
                    RegEntry e = Entries[0];
                    using (RegistryKey k = Registry.CurrentUser.OpenSubKey(e.Path))
                    {
                        if (k == null) return false;
                        object v = k.GetValue(e.Name);
                        return v != null && Convert.ToInt32(v) == e.DebloatVal;
                    }
                }
                catch { return false; }
            }

            public void Set(bool debloat)
            {
                foreach (RegEntry e in Entries)
                {
                    try
                    {
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(e.Path))
                            k.SetValue(e.Name, debloat ? e.DebloatVal : e.DefaultVal, RegistryValueKind.DWord);
                    }
                    catch { }
                }
            }
        }

        private const string CDM = "Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager";

        public static List<Tweak> All()
        {
            return new List<Tweak>
            {
                new Tweak {
                    Title = Lang.T("Отключить Copilot", "Disable Copilot"),
                    Desc = Lang.T("Убирает кнопку и панель Windows Copilot", "Removes the Windows Copilot button and panel"),
                    Entries = new[] { new RegEntry("Software\\Policies\\Microsoft\\Windows\\WindowsCopilot", "TurnOffWindowsCopilot", 1, 0) }
                },
                new Tweak {
                    Title = Lang.T("Отключить Recall (анализ ИИ)", "Disable Recall (AI analysis)"),
                    Desc = Lang.T("Запрещает Windows сохранять снимки экрана для ИИ", "Stops Windows from saving screen snapshots for AI"),
                    Entries = new[] { new RegEntry("Software\\Policies\\Microsoft\\Windows\\WindowsAI", "DisableAIDataAnalysis", 1, 0) }
                },
                new Tweak {
                    Title = Lang.T("Реклама и предложения в Пуске", "Ads & suggestions in Start"),
                    Desc = Lang.T("Отключает рекламные плитки и «рекомендации»", "Turns off promoted tiles and suggestions"),
                    Entries = new[] {
                        new RegEntry(CDM, "SystemPaneSuggestionsEnabled", 0, 1),
                        new RegEntry(CDM, "SubscribedContent-338388Enabled", 0, 1),
                        new RegEntry(CDM, "SubscribedContent-338389Enabled", 0, 1)
                    }
                },
                new Tweak {
                    Title = Lang.T("Реклама на экране блокировки", "Lock screen ads"),
                    Desc = Lang.T("Отключает «интересное» и подсказки на локскрине", "Turns off Spotlight ads and tips on the lock screen"),
                    Entries = new[] {
                        new RegEntry(CDM, "RotatingLockScreenOverlayEnabled", 0, 1),
                        new RegEntry(CDM, "SubscribedContent-338387Enabled", 0, 1)
                    }
                },
                new Tweak {
                    Title = Lang.T("Убрать виджеты с панели задач", "Remove taskbar widgets"),
                    Desc = Lang.T("Прячет кнопку виджетов (погода/новости)", "Hides the widgets button (weather/news)"),
                    Entries = new[] { new RegEntry("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarDa", 0, 1) }
                },
                new Tweak {
                    Title = Lang.T("Отключить Bing в поиске Пуска", "Disable Bing in Start search"),
                    Desc = Lang.T("Поиск не лезет в интернет за подсказками", "Search stops querying the web for suggestions"),
                    Entries = new[] { new RegEntry("Software\\Policies\\Microsoft\\Windows\\Explorer", "DisableSearchBoxSuggestions", 1, 0) }
                },
            };
        }
    }
}
