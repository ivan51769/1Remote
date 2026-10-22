using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using _1RM.Controls;
using Xunit;

namespace Tests;

public class DesktopPreviewPanelTests
{
    [Theory]
    [InlineData(876)]
    [InlineData(1380)]
    public void SwitchingColumnsReflowsMixedHeightCardsWithoutOverlap(double width)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var panel = new DesktopPreviewPanel();
                Assert.Equal(4, panel.Columns);
                var cards = Enumerable.Range(0, 19).Select(i => new Border { Height = 120 + i % 3 * 40 }).ToArray();
                foreach (var card in cards) panel.Children.Add(card);
                foreach (var columns in new[] { 4, 5, 6, 4 })
                {
                    panel.Columns = columns;
                    panel.Measure(new Size(width, double.PositiveInfinity));
                    panel.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
                    for (int i = 0; i < cards.Length; i++)
                    {
                        var pos = cards[i].TranslatePoint(new Point(), panel);
                        Assert.Equal(width / columns, cards[i].ActualWidth, 5);
                        Assert.Equal(i % columns * width / columns, pos.X, 5);
                        Assert.True(pos.X + cards[i].ActualWidth <= width + 0.01);
                        if (i >= columns)
                        {
                            var above = cards[i - columns];
                            Assert.True(pos.Y >= above.TranslatePoint(new Point(), panel).Y + above.ActualHeight);
                        }
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
