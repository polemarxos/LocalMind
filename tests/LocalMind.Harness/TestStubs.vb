' The real Localizer lives in the WinForms app; the console harness only needs a pass-through.
Public Module Localizer
    Public Function T(text As String) As String
        Return text
    End Function
End Module

' The chat page's colors follow the chosen look in the app; the harness keeps the default.
Public Module Theme
    Public Function MapCss(css As String) As String
        Return css
    End Function
End Module
