using System.Windows;
using System.Windows.Media;

namespace KubeUIApp.Services
{
    public static class ThemeService
    {
        public static bool IsDarkTheme { get; private set; } = true;

        public static void ApplyTheme(bool isDark)
        {
            IsDarkTheme = isDark;
            var res = Application.Current.Resources;

            if (isDark)
            {
                // Dark Theme (Modern Slate / Catppuccin Mocha)
                res["BgDarkBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
                res["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181825"));
                res["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252538"));
                res["RibbonBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14141E"));
                res["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244"));
                res["TextLightBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
                res["TextMutedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8"));
                res["InputBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252538"));
                res["InputBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#45475A"));
                res["DataGridRowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181825"));
                res["DataGridRowAltBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E1E2E"));
                res["DataGridHeaderBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252538"));
                res["DataGridLineBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28283D"));
                res["BtnNormalBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#313244"));
                res["BtnNormalFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
                res["TabActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252538"));
                res["TerminalBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#11111B"));
                res["TerminalFgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1"));
                res["AccentPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#89B4FA"));
                res["AccentSuccessBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                res["AccentWarningBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                res["AccentDangerBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8"));
                res["RowSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B4261"));
                res["RowSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                res["RowHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2E44"));
            }
            else
            {
                // Light Theme (Clean Modern Enterprise / GitHub Light)
                res["BgDarkBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6"));
                res["SurfaceBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                res["CardBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                res["RibbonBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
                res["BorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
                res["TextLightBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                res["TextMutedBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
                res["InputBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                res["InputBorderBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                res["DataGridRowBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
                res["DataGridRowAltBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                res["DataGridHeaderBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                res["DataGridLineBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                res["BtnNormalBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                res["BtnNormalFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                res["TabActiveBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                res["TerminalBgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                res["TerminalFgBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4ADE80"));
                res["AccentPrimaryBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
                res["AccentSuccessBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                res["AccentWarningBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                res["AccentDangerBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                res["RowSelectedBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DBEAFE"));
                res["RowSelectedFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
                res["RowHoverBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
            }
        }
    }
}
