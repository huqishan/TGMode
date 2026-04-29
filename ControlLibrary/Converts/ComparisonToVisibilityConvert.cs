using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace ControlLibrary.Converts
{
    public class ComparisonToVisibilityConvert : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string? valueText = value?.ToString();
            string? parameterText = parameter?.ToString();
            if (string.IsNullOrWhiteSpace(valueText) || string.IsNullOrWhiteSpace(parameterText))
            {
                return Visibility.Collapsed;
            }

            return parameterText.ToUpperInvariant().Contains(valueText.ToUpperInvariant())
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
