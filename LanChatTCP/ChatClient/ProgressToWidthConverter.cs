using System;
using System.Globalization;
using System.Windows.Data;

namespace ChatClient
{
    public class ProgressToWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 && 
                values[0] is double currentValue && 
                values[1] is double maximum && 
                values[2] is double actualWidth)
            {
                if (maximum > 0 && actualWidth > 0)
                {
                    return actualWidth * (currentValue / maximum);
                }
            }
            return 0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
