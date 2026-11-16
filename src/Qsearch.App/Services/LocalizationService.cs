using System.Globalization;
using System.Windows;

namespace Qsearch.App.Services;

public sealed class LocalizationService
{
    private ResourceDictionary? _activeDictionary;

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public void Apply(string cultureName)
    {
        CultureInfo culture = CultureInfo.GetCultureInfo(cultureName);
        string resource = culture.TwoLetterISOLanguageName == "fa"
            ? "Resources/Strings.fa.xaml"
            : "Resources/Strings.en.xaml";
        ResourceDictionary dictionary = new()
        {
            Source = new Uri(
                $"pack://application:,,,/Qsearch;component/{resource}",
                UriKind.Absolute),
        };
        if (_activeDictionary is not null)
        {
            System.Windows.Application.Current.Resources.MergedDictionaries.Remove(_activeDictionary);
        }

        System.Windows.Application.Current.Resources.MergedDictionaries.Add(dictionary);
        _activeDictionary = dictionary;
        CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
