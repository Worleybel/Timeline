using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Timline.Windows;

/// <summary>[yyyy]-[MM]-[dd] 分段输入的日期行：仅数字、输满自动跳下一段、失焦归一化、方向键跨段。</summary>
public partial class DateRow : UserControl
{
    private const int MinYear = 2000;

    private bool _syncing;

    public DateRow()
    {
        InitializeComponent();
        Value = DateOnly.FromDateTime(DateTime.Today);
    }

    /// <summary>当前日期；非法片段按边界收敛（如 2 月 31 日 → 2 月末天）。</summary>
    public DateOnly Value
    {
        get
        {
            var year = ParseSegment(TbYear.Text, MinYear, 9999);
            var month = ParseSegment(TbMonth.Text, 1, 12);
            var day = ParseSegment(TbDay.Text, 1, DateTime.DaysInMonth(year, month));
            return new DateOnly(year, month, day);
        }
        set
        {
            _syncing = true;
            TbYear.Text = $"{value.Year:D4}";
            TbMonth.Text = $"{value.Month:D2}";
            TbDay.Text = $"{value.Day:D2}";
            _syncing = false;
        }
    }

    private static int ParseSegment(string text, int min, int max)
    {
        var v = text.Length > 0 && int.TryParse(text, out var n) ? n : min;
        return Math.Clamp(v, min, max);
    }

    private void DigitOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsDigit);

    private void SegmentGotFocus(object sender, RoutedEventArgs e) =>
        ((TextBox)sender).SelectAll();

    private void YearTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TbYear.Text.Length != 4) return;
        AdvanceTo(TbMonth, caretAtEnd: false);
    }

    private void MonthTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TbMonth.Text.Length != 2) return;
        ClampSegment(TbMonth, 1, 12);
        AdvanceTo(TbDay, caretAtEnd: false);
    }

    private void DayTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || TbDay.Text.Length != 2) return;
        ClampSegment(TbDay, 1, DateTime.DaysInMonth(Value.Year, Value.Month));
    }

    private void SegmentLostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        _syncing = true;
        box.Text = box == TbYear
            ? $"{ParseSegment(box.Text, MinYear, 9999):D4}"
            : $"{ParseSegment(box.Text, 1, 99):D2}";
        _syncing = false;
        if (box == TbMonth) ClampSegment(TbMonth, 1, 12);
        else if (box == TbDay) ClampSegment(TbDay, 1, DateTime.DaysInMonth(Value.Year, Value.Month));
    }

    private void YearKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right && TbYear.CaretIndex == TbYear.Text.Length)
        {
            AdvanceTo(TbMonth, caretAtEnd: false);
            e.Handled = true;
        }
    }

    private void MonthKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Right && TbMonth.CaretIndex == TbMonth.Text.Length)
        {
            AdvanceTo(TbDay, caretAtEnd: false);
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.Back && TbMonth.Text.Length == 0)
            || (e.Key == Key.Left && TbMonth.CaretIndex == 0))
        {
            AdvanceTo(TbYear, caretAtEnd: true);
            e.Handled = true;
        }
    }

    private void DayKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key == Key.Back && TbDay.Text.Length == 0)
            || (e.Key == Key.Left && TbDay.CaretIndex == 0))
        {
            AdvanceTo(TbMonth, caretAtEnd: true);
            e.Handled = true;
        }
    }

    private void ClampSegment(TextBox box, int min, int max)
    {
        if (!int.TryParse(box.Text, out var v) || v >= min && v <= max) return;
        _syncing = true;
        box.Text = $"{Math.Clamp(v, min, max):D2}";
        _syncing = false;
        box.CaretIndex = box.Text.Length;
    }

    private void AdvanceTo(TextBox next, bool caretAtEnd)
    {
        next.Focus();
        if (caretAtEnd) next.CaretIndex = next.Text.Length;
        // 否则保留 GotFocus 的全选行为：直接输入即覆盖整段
    }
}
