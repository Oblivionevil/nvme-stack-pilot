using System;
using System.Windows.Markup;

namespace NvmeDriverSwitch.Infrastructure
{
    /// <summary>Markup-Extension fuer lokalisierte XAML-Texte: {l:Loc KeyName}.</summary>
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension()
        {
        }

        public LocExtension(string key)
        {
            Key = key;
        }

        public string Key { get; set; }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return LocalizedStrings.Get(Key);
        }
    }
}
