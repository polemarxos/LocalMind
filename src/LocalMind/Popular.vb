Imports System.Windows.Forms

''' <summary>
''' "Show the most popular" for the long model lists: a small combo (Top 30 / 50 / 100 / All)
''' and the filter that keeps only the most downloaded models (installed ones are always kept).
''' </summary>
Public Module Popular
    Public ReadOnly Sizes As Integer() = {15, 30, 50, 100, 0}

    Public Function MakeCombo(Optional width As Integer = 170) As ComboBox
        Dim c As New ComboBox() With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = width, .Margin = New Padding(3, 6, 3, 3)}
        For Each n In Sizes
            c.Items.Add(If(n = 0, Localizer.T("All (any popularity)"), String.Format(Localizer.T("⭐ Top {0} most popular"), n)))
        Next
        Dim i = Array.IndexOf(Sizes, AppCore.Settings.ShowTop)
        c.SelectedIndex = If(i < 0, 1, i)
        Theme.StyleInput(c)
        AddHandler c.SelectedIndexChanged, Sub()
                                               AppCore.Settings.ShowTop = Sizes(Math.Max(0, c.SelectedIndex))
                                               AppCore.SaveSettings()
                                           End Sub
        Return c
    End Function

    Public Function Count(c As ComboBox) As Integer
        If c Is Nothing OrElse c.SelectedIndex < 0 Then Return 0
        Return Sizes(c.SelectedIndex)
    End Function

    ''' <summary>The n most downloaded items, in their original order; items for which keep() is true always stay.</summary>
    Public Function Top(Of T)(items As IEnumerable(Of T), n As Integer, downloads As Func(Of T, Long), Optional keep As Func(Of T, Boolean) = Nothing) As List(Of T)
        Dim all = items.ToList()
        If n <= 0 OrElse all.Count <= n Then Return all
        Dim best = New HashSet(Of T)(all.OrderByDescending(downloads).Take(n))
        Return all.Where(Function(x) best.Contains(x) OrElse (keep IsNot Nothing AndAlso keep(x))).ToList()
    End Function
End Module
