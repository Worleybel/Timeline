using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Timline.Windows;

/// <summary>[HH]:[mm] 分段输入的时间行：仅数字、输满两位自动跳下一段、失焦补零、方向键跨段。</summary>
public partial class TimePickerRow : UserControl
{
    private string _title = "";
    private bool _syncing;

    public TimePickerRow()
    {
        InitializeComponent();
        TbHour.Text = "09";
        TbMinute.Text = "00";
    }

    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Lbl.Text = value;
        }
    }

    public TimeOnly Value
    {
        get => new(ParseSegment(TbHour.Text, 23), ParseSegment(TbMinute.Text, 59));
        set
        {
            TbHour.Text = $"{value.Hour:D2}";
            TbMinute.Text = $"{value.Minute:D2}";
        }
    }

    private static int ParseSegment(string text, int max)
    {
        var v = text.Length > 0 && int.TryParse(text, out var n) ? n : 0;
        return Math.Clamp(v, 0, max);
    }

    private void DigitOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void SegmentGotFocus(object sender, RoutedEventArgs e) =>
        ((TextBox)sender).SelectAll();

    private void HourTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TbHour.Text.Length != 2) return;
        ClampSegment(TbHour, 23);
        AdvanceTo(TbMinute);
    }

    private void MinuteTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TbMinute.Text.Length != 2) return;
        ClampSegment(TbMinute, 59);
    }

    private void SegmentLostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var max = box == TbHour ? 23 : 59;
        _syncing = true;
        box.Text = $"{ParseSegment(box.Text, max):D2}";
        _syncing = false;
    }

    private void HourKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right && TbHour.CaretIndex == TbHour.Text.Length)
        {
            AdvanceTo(TbMinute);
            e.Handled = true;
        }
    }

    private void MinuteKeyDown(object sender, KeyEventArgs e)
    {
        var toHour = (e.Key == Key.Back && TbMinute.Text.Length == 0)
                     || (e.Key == Key.Left && TbMinute.CaretIndex == 0);
        if (toHour)
        {
            TbHour.Focus();
            TbHour.CaretIndex = TbHour.Text.Length;
            e.Handled = true;
        }
    }

    private void ClampSegment(TextBox box, int max)
    {
        if (!int.TryParse(box.Text, out var v) || v <= max) return;
        _syncing = true;
        box.Text = $"{max:D2}";
        _syncing = false;
        box.CaretIndex = box.Text.Length;
    }

    private void AdvanceTo(TextBox next)
    {
        next.Focus();
        next.SelectAll();
    }
}
