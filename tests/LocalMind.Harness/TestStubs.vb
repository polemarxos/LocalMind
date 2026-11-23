' The real Localizer lives in the WinForms app; the console harness only needs a pass-through.
Public Module Localizer
    Public Function T(text As String) As String
        Return text
    End Function
End Module
